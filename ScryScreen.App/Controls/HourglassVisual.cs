using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ScryScreen.App.Controls;

public sealed class HourglassVisual : Control
{
    public static readonly StyledProperty<double> FractionRemainingProperty =
        AvaloniaProperty.Register<HourglassVisual, double>(nameof(FractionRemaining), 1.0);

    public static readonly StyledProperty<bool> IsRunningProperty =
        AvaloniaProperty.Register<HourglassVisual, bool>(nameof(IsRunning), true);

    private readonly DispatcherTimer _renderTimer;
    private DateTime _lastRenderTickUtc;
    private readonly Random _rng = new(0x5C_52_59_01);

    private const int GrainCount = 1000;
    private readonly List<Grain> _grains = new(GrainCount);
    private readonly Dictionary<long, List<int>> _grid = new();
    private readonly SolidColorBrush[] _sandBrushRamp = new SolidColorBrush[18];

    private double _lastFrac = 1.0;
    private double _lastW;
    private double _lastH;
    private bool _needsReseed = true;
    private double _releaseCarry;

    public double FractionRemaining
    {
        get => GetValue(FractionRemainingProperty);
        set => SetValue(FractionRemainingProperty, value);
    }

    public bool IsRunning
    {
        get => GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    public HourglassVisual()
    {
        _renderTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => TickRender());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _lastRenderTickUtc = DateTime.UtcNow;
        _renderTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _renderTimer.Stop();
        _grains.Clear();
        _grid.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void TickRender()
    {
        if (!IsEffectivelyVisible)
        {
            _lastRenderTickUtc = DateTime.UtcNow;
            return;
        }

        var now = DateTime.UtcNow;
        var dt = now - _lastRenderTickUtc;
        _lastRenderTickUtc = now;

        if (dt < TimeSpan.Zero || dt > TimeSpan.FromSeconds(0.25))
        {
            dt = TimeSpan.FromMilliseconds(16);
        }

        UpdatePhysics(dt.TotalSeconds);
        InvalidateVisual();
    }

    private void UpdatePhysics(double dtSeconds)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 4 || h <= 4)
        {
            return;
        }

        var frac = GetClampedFraction();
        EnsureInitialized(w, h, frac);

        // If the timer just ended, aggressively drain any remaining grains so the top ends empty.
        if (!IsRunning && frac <= 0.0001 && _lastFrac > 0.01)
        {
            DrainTopIntoBottom(w, h);
        }

        if (!IsRunning)
        {
            _lastFrac = frac;
            return;
        }

        var geom = GetGeometry(w, h);
        ReleaseGrainsFromTop(dtSeconds, frac, geom);

        // Verlet integration.
        var gravity = 3000.0;
        var damping = 0.012;
        var dt2 = dtSeconds * dtSeconds;

        for (var i = 0; i < _grains.Count; i++)
        {
            var g = _grains[i];

            if (!g.Active)
            {
                continue;
            }

            var vx = (g.X - g.PrevX) * (1.0 - damping);
            var vy = (g.Y - g.PrevY) * (1.0 - damping);

            g.PrevX = g.X;
            g.PrevY = g.Y;

            // Very small jitter keeps the stream from looking perfectly uniform.
            var jitter = (0.5 - _rng.NextDouble()) * 1.5;

            g.X += vx + jitter;
            g.Y += vy + gravity * dt2;

            _grains[i] = g;
        }

        // Constraint iterations.
        const int iterations = 4;
        for (var it = 0; it < iterations; it++)
        {
            ConstrainToHourglass(geom);
            SolveParticleCollisions();
        }

        _lastFrac = frac;
    }

    private double GetClampedFraction()
    {
        var frac = FractionRemaining;
        if (double.IsNaN(frac) || double.IsInfinity(frac)) frac = 0;
        if (frac < 0) frac = 0;
        if (frac > 1) frac = 1;
        return frac;
    }

    private void EnsureInitialized(double w, double h, double frac)
    {
        var sizeChanged = Math.Abs(_lastW - w) > 0.5 || Math.Abs(_lastH - h) > 0.5;
        var fracIncreased = frac > _lastFrac + 0.10;

        if (_needsReseed || sizeChanged || (_grains.Count != GrainCount) || (!IsRunning && fracIncreased))
        {
            _lastW = w;
            _lastH = h;
            _needsReseed = false;
            _releaseCarry = 0;
            ReseedGrains(w, h, frac);
        }
    }

    private void ReseedGrains(double w, double h, double frac)
    {
        _grains.Clear();
        var geom = GetGeometry(w, h);

        var topCount = (int)Math.Round(GrainCount * frac);
        if (topCount < 0) topCount = 0;
        if (topCount > GrainCount) topCount = GrainCount;

        var r = geom.GrainR;

        // Populate top as a packed pile (inactive until released).
        FillPackedTop(geom, r, topCount);

        // Populate bottom as a packed pile (already released/active).
        FillPackedBottom(geom, r, GrainCount - topCount);

        // Quick relaxation so it starts as a pile instead of a cloud.
        for (var it = 0; it < 22; it++)
        {
            ConstrainToHourglass(geom);
            SolveParticleCollisions();
        }
    }

    private void DrainTopIntoBottom(HourglassGeom geom)
    {
        // Move any grains still in the top chamber down into the bottom chamber.
        var r = geom.GrainR;
        for (var i = 0; i < _grains.Count; i++)
        {
            var g = _grains[i];
            if (!g.Active || g.Y < geom.NeckY0)
            {
                var p = RandomPointInBottom(geom, r);
                g.X = p.X;
                g.Y = p.Y;
                g.PrevX = g.X;
                g.PrevY = g.Y;
                g.Active = true;
                _grains[i] = g;
            }
        }

        for (var it = 0; it < 18; it++)
        {
            ConstrainToHourglass(geom);
            SolveParticleCollisions();
        }
    }

    private void DrainTopIntoBottom(double w, double h)
        => DrainTopIntoBottom(GetGeometry(w, h));

    private void ReleaseGrainsFromTop(double dtSeconds, double frac, HourglassGeom geom)
    {
        // Release rate is derived from how fast FractionRemaining decreases.
        // This automatically matches the user's chosen timer duration.
        var deltaFrac = _lastFrac - frac;
        if (deltaFrac <= 0)
        {
            return;
        }

        var desired = (deltaFrac * GrainCount) + _releaseCarry;
        var toRelease = (int)Math.Floor(desired);
        _releaseCarry = desired - toRelease;

        if (toRelease <= 0)
        {
            return;
        }

        // Avoid huge bursts on lag spikes.
        toRelease = Math.Min(toRelease, 12);
        for (var i = 0; i < toRelease; i++)
        {
            if (!ReleaseOneGrain(geom, dtSeconds))
            {
                break;
            }
        }
    }

    private bool ReleaseOneGrain(HourglassGeom geom, double dtSeconds)
    {
        // Find an inactive grain closest to the neck (largest Y) and release it.
        var bestIndex = -1;
        var bestY = double.NegativeInfinity;
        for (var i = 0; i < _grains.Count; i++)
        {
            var g = _grains[i];
            if (g.Active)
            {
                continue;
            }

            if (g.Y > bestY)
            {
                bestY = g.Y;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
        {
            return false;
        }

        var r = geom.GrainR;
        var x = geom.Cx + (geom.Rng.NextDouble() - 0.5) * (geom.NeckHalfW * 0.65);
        var y = geom.NeckY0 + r + (geom.Rng.NextDouble() * r * 0.8);

        var grain = _grains[bestIndex];
        grain.Active = true;
        grain.X = x;
        grain.Y = y;

        // Give it a stable downward initial velocity.
        var vy0 = 350.0;
        var vx0 = (geom.Rng.NextDouble() - 0.5) * 30.0;
        grain.PrevX = grain.X - (vx0 * dtSeconds);
        grain.PrevY = grain.Y - (vy0 * dtSeconds);

        _grains[bestIndex] = grain;
        return true;
    }

    private static Point RandomPointInTop(HourglassGeom g, double r)
    {
        // Uniform-ish random within trapezoid bounds by sampling y then clamping x range.
        var y = g.TopY0 + r + (g.ChamberH - (2 * r)) * g.Rng.NextDouble();
        var t = (y - g.TopY0) / (g.TopY1 - g.TopY0);
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        var half = Lerp(g.TopHalfW - r, g.NeckHalfW - r, t);
        var x = (g.Cx - half) + (2 * half) * g.Rng.NextDouble();
        return new Point(x, y);
    }

    private static Point RandomPointInBottom(HourglassGeom g, double r)
    {
        // Start closer to the bottom so it looks like a pile.
        var yBias = Math.Pow(g.Rng.NextDouble(), 0.35);
        var y = g.BotY1 - r - (g.ChamberH - (2 * r)) * yBias;
        var t = (y - g.BotY0) / (g.BotY1 - g.BotY0);
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        var half = Lerp(g.NeckHalfW - r, g.BotHalfW - r, t);
        var x = (g.Cx - half) + (2 * half) * g.Rng.NextDouble();
        return new Point(x, y);
    }

    private void ConstrainToHourglass(HourglassGeom geom)
    {
        var r = geom.GrainR;
        var topMinY = geom.TopY0 + r;
        var topMaxY = geom.NeckY0 - r;
        var neckMinY = geom.NeckY0 + r;
        var neckMaxY = geom.NeckY1 - r;
        var botMinY = geom.BotY0 + r;
        var botMaxY = geom.BotY1 - r;

        for (var i = 0; i < _grains.Count; i++)
        {
            var p = _grains[i];

            if (!p.Active)
            {
                // Inactive grains stay in the top chamber (a static pile).
                // Clamp gently in case of resize.
                if (p.Y < topMinY) p.Y = topMinY;
                if (p.Y > topMaxY) p.Y = topMaxY;
                var tt = (p.Y - geom.TopY0) / (geom.TopY1 - geom.TopY0);
                if (tt < 0) tt = 0;
                if (tt > 1) tt = 1;
                var halfT = Lerp(geom.TopHalfW - r, geom.NeckHalfW - r, tt);
                var minXT = geom.Cx - halfT;
                var maxXT = geom.Cx + halfT;
                if (p.X < minXT) p.X = minXT;
                if (p.X > maxXT) p.X = maxXT;
                p.PrevX = p.X;
                p.PrevY = p.Y;
                _grains[i] = p;
                continue;
            }

            if (p.Y < geom.NeckY0)
            {
                // Top trapezoid.
                if (p.Y < topMinY) { p.Y = topMinY; p.PrevY = p.Y; }
                if (p.Y > topMaxY) { p.Y = topMaxY; }
                var t = (p.Y - geom.TopY0) / (geom.TopY1 - geom.TopY0);
                if (t < 0) t = 0;
                if (t > 1) t = 1;
                var half = Lerp(geom.TopHalfW - r, geom.NeckHalfW - r, t);
                var minX = geom.Cx - half;
                var maxX = geom.Cx + half;
                if (p.X < minX) { p.X = minX; p.PrevX = p.X; }
                if (p.X > maxX) { p.X = maxX; p.PrevX = p.X; }
            }
            else if (p.Y < geom.NeckY1)
            {
                // Neck rectangle.
                if (p.Y < neckMinY) { p.Y = neckMinY; p.PrevY = p.Y; }
                if (p.Y > neckMaxY) { p.Y = neckMaxY; }
                var half = geom.NeckHalfW - r;
                var minX = geom.Cx - half;
                var maxX = geom.Cx + half;
                if (p.X < minX) { p.X = minX; p.PrevX = p.X; }
                if (p.X > maxX) { p.X = maxX; p.PrevX = p.X; }
            }
            else
            {
                // Bottom trapezoid.
                if (p.Y < botMinY) { p.Y = botMinY; p.PrevY = p.Y; }
                if (p.Y > botMaxY) { p.Y = botMaxY; p.PrevY = p.Y; }
                var t = (p.Y - geom.BotY0) / (geom.BotY1 - geom.BotY0);
                if (t < 0) t = 0;
                if (t > 1) t = 1;
                var half = Lerp(geom.NeckHalfW - r, geom.BotHalfW - r, t);
                var minX = geom.Cx - half;
                var maxX = geom.Cx + half;
                if (p.X < minX) { p.X = minX; p.PrevX = p.X; }
                if (p.X > maxX) { p.X = maxX; p.PrevX = p.X; }
            }

            _grains[i] = p;
        }
    }

    private void SolveParticleCollisions()
    {
        if (_grains.Count <= 1)
        {
            return;
        }

        // Reuse lists to avoid per-frame allocations.
        foreach (var list in _grid.Values)
        {
            list.Clear();
        }

        if (_grid.Count > 2500)
        {
            _grid.Clear();
        }

        var r = _grains[0].R;
        var cellSize = Math.Max(4.0, r * 2.6);

        for (var i = 0; i < _grains.Count; i++)
        {
            var p = _grains[i];
            if (!p.Active)
            {
                continue;
            }
            var cx = (int)Math.Floor(p.X / cellSize);
            var cy = (int)Math.Floor(p.Y / cellSize);
            var key = Pack(cx, cy);

            if (!_grid.TryGetValue(key, out var list))
            {
                list = new List<int>(16);
                _grid[key] = list;
            }

            list.Add(i);
        }

        for (var i = 0; i < _grains.Count; i++)
        {
            var a = _grains[i];
            if (!a.Active)
            {
                continue;
            }
            var cx = (int)Math.Floor(a.X / cellSize);
            var cy = (int)Math.Floor(a.Y / cellSize);

            for (var oy = -1; oy <= 1; oy++)
            {
                for (var ox = -1; ox <= 1; ox++)
                {
                    var key = Pack(cx + ox, cy + oy);
                    if (!_grid.TryGetValue(key, out var list))
                    {
                        continue;
                    }

                    for (var li = 0; li < list.Count; li++)
                    {
                        var j = list[li];
                        if (j <= i)
                        {
                            continue;
                        }

                        var b = _grains[j];
                        if (!b.Active)
                        {
                            continue;
                        }
                        var dx = b.X - a.X;
                        var dy = b.Y - a.Y;
                        var minDist = a.R + b.R;
                        var dist2 = (dx * dx) + (dy * dy);
                        if (dist2 <= 0.0001 || dist2 >= (minDist * minDist))
                        {
                            continue;
                        }

                        var dist = Math.Sqrt(dist2);
                        var overlap = minDist - dist;
                        var nx = dx / dist;
                        var ny = dy / dist;

                        // Push equally apart.
                        var push = overlap * 0.5;
                        a.X -= nx * push;
                        a.Y -= ny * push;
                        b.X += nx * push;
                        b.Y += ny * push;

                        _grains[i] = a;
                        _grains[j] = b;
                    }
                }
            }
        }
    }

    private static long Pack(int x, int y)
        => ((long)x << 32) ^ (uint)y;

    private static double Lerp(double a, double b, double t)
        => a + ((b - a) * t);

    private SolidColorBrush GetSandBrush(double t)
    {
        if (t < 0) t = 0;
        if (t > 1) t = 1;

        var idx = (int)Math.Round(t * (_sandBrushRamp.Length - 1));
        if (idx < 0) idx = 0;
        if (idx >= _sandBrushRamp.Length) idx = _sandBrushRamp.Length - 1;

        var brush = _sandBrushRamp[idx];
        if (brush is not null)
        {
            return brush;
        }

        // Slightly brighter toward the top; darker toward the bottom.
        var inv = 1.0 - (idx / (double)(_sandBrushRamp.Length - 1));
        var rCol = (byte)Math.Clamp(232 + (12 * inv), 0, 255);
        var gCol = (byte)Math.Clamp(160 + (48 * inv), 0, 255);
        var bCol = (byte)Math.Clamp(72 + (58 * inv), 0, 255);
        var aCol = (byte)210;

        brush = new SolidColorBrush(Color.FromArgb(aCol, rCol, gCol, bCol));
        _sandBrushRamp[idx] = brush;
        return brush;
    }

    private readonly struct HourglassGeom
    {
        public readonly double W;
        public readonly double H;
        public readonly double Cx;
        public readonly double M;
        public readonly double NeckH;
        public readonly double ChamberH;
        public readonly double TopY0;
        public readonly double TopY1;
        public readonly double NeckY0;
        public readonly double NeckY1;
        public readonly double BotY0;
        public readonly double BotY1;
        public readonly double TopHalfW;
        public readonly double NeckHalfW;
        public readonly double BotHalfW;
        public readonly double GrainR;
        public readonly Random Rng;

        public HourglassGeom(double w, double h, Random rng)
        {
            W = w;
            H = h;
            Rng = rng;

            Cx = w / 2.0;
            M = Math.Max(6, Math.Min(w, h) * 0.06);
            NeckH = Math.Max(10, h * 0.10);
            ChamberH = (h - NeckH - (M * 2)) / 2.0;
            if (ChamberH <= 2)
            {
                ChamberH = Math.Max(2, (h - (M * 2)) / 2.0);
            }

            TopY0 = M;
            TopY1 = TopY0 + ChamberH;
            NeckY0 = TopY1;
            NeckY1 = NeckY0 + NeckH;
            BotY0 = NeckY1;
            BotY1 = BotY0 + ChamberH;

            var topW = w - (M * 2);
            var neckW = Math.Max(10, w * 0.10);
            TopHalfW = topW / 2.0;
            NeckHalfW = neckW / 2.0;
            BotHalfW = TopHalfW;

            GrainR = Math.Clamp(Math.Min(w, h) * 0.0065, 1.4, 4.2);
        }
    }

    private HourglassGeom GetGeometry(double w, double h)
        => new(w, h, _rng);

    private struct Grain
    {
        public double X;
        public double Y;
        public double PrevX;
        public double PrevY;
        public double R;
        public bool Active;

        public Grain(double x, double y, double r)
        {
            X = x;
            Y = y;
            PrevX = x;
            PrevY = y;
            R = r;
            Active = true;
        }
    }

    private void FillPackedTop(HourglassGeom geom, double r, int count)
    {
        if (count <= 0)
        {
            return;
        }

        // Pack starting near the neck (bottom of top chamber) and build upward.
        var spacingX = r * 2.05;
        var spacingY = r * 1.95;
        var y = geom.TopY1 - r;

        while (_grains.Count < count && y > geom.TopY0 + r)
        {
            var t = (y - geom.TopY0) / (geom.TopY1 - geom.TopY0);
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            var half = Lerp(geom.TopHalfW - r, geom.NeckHalfW - r, t);

            for (var x = geom.Cx - half; x <= geom.Cx + half && _grains.Count < count; x += spacingX)
            {
                var gx = x + (geom.Rng.NextDouble() - 0.5) * r * 0.35;
                var gy = y + (geom.Rng.NextDouble() - 0.5) * r * 0.25;
                var g = new Grain(gx, gy, r) { Active = false };
                _grains.Add(g);
            }

            y -= spacingY;
        }

        // If we couldn't pack enough (very small sizes), fill the rest randomly.
        while (_grains.Count < count)
        {
            var p = RandomPointInTop(geom, r);
            var g = new Grain(p.X, p.Y, r) { Active = false };
            _grains.Add(g);
        }
    }

    private void FillPackedBottom(HourglassGeom geom, double r, int count)
    {
        if (count <= 0)
        {
            return;
        }

        var spacingX = r * 2.05;
        var spacingY = r * 1.95;
        var filled = 0;
        var y = geom.BotY1 - r;

        while (filled < count && y > geom.BotY0 + r)
        {
            var t = (y - geom.BotY0) / (geom.BotY1 - geom.BotY0);
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            var half = Lerp(geom.NeckHalfW - r, geom.BotHalfW - r, t);

            for (var x = geom.Cx - half; x <= geom.Cx + half && filled < count; x += spacingX)
            {
                var gx = x + (geom.Rng.NextDouble() - 0.5) * r * 0.35;
                var gy = y + (geom.Rng.NextDouble() - 0.5) * r * 0.25;
                var g = new Grain(gx, gy, r) { Active = true };
                _grains.Add(g);
                filled++;
            }

            y -= spacingY;
        }

        while (filled < count)
        {
            var p = RandomPointInBottom(geom, r);
            var g = new Grain(p.X, p.Y, r) { Active = true };
            _grains.Add(g);
            filled++;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 4 || h <= 4)
        {
            return;
        }

        var frac = GetClampedFraction();

        // Layout.
        var cx = w / 2.0;
        var m = Math.Max(6, Math.Min(w, h) * 0.06);
        var neckH = Math.Max(10, h * 0.10);
        var chamberH = (h - neckH - (m * 2)) / 2.0;
        if (chamberH <= 2)
        {
            chamberH = Math.Max(2, (h - (m * 2)) / 2.0);
        }

        var topY0 = m;
        var topY1 = topY0 + chamberH;
        var neckY0 = topY1;
        var neckY1 = neckY0 + neckH;
        var botY0 = neckY1;
        var botY1 = botY0 + chamberH;

        var topW = w - (m * 2);
        var botW = topW;
        var neckW = Math.Max(10, w * 0.10);
        var curve = Math.Min(topW, chamberH) * 0.22;

        // Outer glass silhouette (curved) + a thin fantasy frame.
        var outline = new StreamGeometry();
        using (var g = outline.Open())
        {
            // Start top-left.
            var p0 = new Point(cx - topW / 2, topY0);
            var p1 = new Point(cx + topW / 2, topY0);
            var p2 = new Point(cx + neckW / 2, topY1);
            var p3 = new Point(cx + neckW / 2, botY0);
            var p4 = new Point(cx + botW / 2, botY1);
            var p5 = new Point(cx - botW / 2, botY1);
            var p6 = new Point(cx - neckW / 2, botY0);
            var p7 = new Point(cx - neckW / 2, topY1);

            g.BeginFigure(p0, isFilled: true);
            g.LineTo(p1);
            g.CubicBezierTo(new Point(cx + topW / 2, topY0 + curve), new Point(cx + neckW / 2 + curve * 0.35, topY1 - curve), p2);
            g.LineTo(new Point(cx + neckW / 2, neckY0));
            g.LineTo(p3);
            g.CubicBezierTo(new Point(cx + neckW / 2 + curve * 0.35, botY0 + curve), new Point(cx + botW / 2, botY1 - curve), p4);
            g.LineTo(p5);
            g.CubicBezierTo(new Point(cx - botW / 2, botY1 - curve), new Point(cx - neckW / 2 - curve * 0.35, botY0 + curve), p6);
            g.LineTo(new Point(cx - neckW / 2, neckY0));
            g.LineTo(p7);
            g.CubicBezierTo(new Point(cx - neckW / 2 - curve * 0.35, topY1 - curve), new Point(cx - topW / 2, topY0 + curve), p0);
            g.EndFigure(isClosed: true);
        }

        var frameStroke = new Pen(new SolidColorBrush(Color.FromArgb(210, 232, 217, 182)), thickness: Math.Max(2, Math.Min(w, h) * 0.018));
        context.DrawGeometry(null, frameStroke, outline);

        // Glass tint and internal glow.
        var glass = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.FromArgb(26, 255, 255, 255), 0.0),
                new GradientStop(Color.FromArgb(10, 255, 255, 255), 0.55),
                new GradientStop(Color.FromArgb(18, 255, 255, 255), 1.0),
            }
        };
        context.DrawGeometry(glass, null, outline);

        // Sand grains.
        // Ensure grain state exists even if Render happens before the first physics tick.
        EnsureInitialized(w, h, frac);
        using (context.PushGeometryClip(outline))
        {
            for (var i = 0; i < _grains.Count; i++)
            {
                var p = _grains[i];
                // Color varies slightly by height to feel more organic.
                var t = Math.Clamp((p.Y - topY0) / (botY1 - topY0), 0, 1);
                var brush = GetSandBrush(t);
                context.DrawEllipse(brush, null, new Point(p.X, p.Y), p.R, p.R);
            }
        }

        // Specular highlights (fantasy glass sheen).
        var highlight = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), thickness: Math.Max(1, Math.Min(w, h) * 0.01));
        context.DrawLine(highlight, new Point(cx - topW * 0.24, topY0 + chamberH * 0.08), new Point(cx - neckW * 0.55, neckY0 + neckH * 0.15));
        context.DrawLine(highlight, new Point(cx + topW * 0.26, topY0 + chamberH * 0.12), new Point(cx + neckW * 0.62, neckY0 + neckH * 0.25));
    }
}
