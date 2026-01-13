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

    public static readonly StyledProperty<double> GravityProperty =
        AvaloniaProperty.Register<HourglassVisual, double>(nameof(Gravity), 2400.0);

    public static readonly StyledProperty<double> DampingProperty =
        AvaloniaProperty.Register<HourglassVisual, double>(nameof(Damping), 0.120);

    public static readonly StyledProperty<double> JitterAccelProperty =
        AvaloniaProperty.Register<HourglassVisual, double>(nameof(JitterAccel), 80.0);

    public static readonly StyledProperty<double> SettleKeepProperty =
        AvaloniaProperty.Register<HourglassVisual, double>(nameof(SettleKeep), 0.18);

    public static readonly StyledProperty<double> SleepThresholdProperty =
        AvaloniaProperty.Register<HourglassVisual, double>(nameof(SleepThreshold), 0.30);

    public static readonly StyledProperty<int> MaxReleasePerFrameProperty =
        AvaloniaProperty.Register<HourglassVisual, int>(nameof(MaxReleasePerFrame), 12);

    public static readonly StyledProperty<int> ParticleCountProperty =
        AvaloniaProperty.Register<HourglassVisual, int>(nameof(ParticleCount), 1000);

    public static readonly StyledProperty<double> GrainRadiusScaleProperty =
        AvaloniaProperty.Register<HourglassVisual, double>(nameof(GrainRadiusScale), 1.0);

    public static readonly StyledProperty<double> CollisionFrictionProperty =
        AvaloniaProperty.Register<HourglassVisual, double>(nameof(CollisionFriction), 0.85);

    public static readonly StyledProperty<double> CollisionStabilizationProperty =
        AvaloniaProperty.Register<HourglassVisual, double>(nameof(CollisionStabilization), 0.95);

    public static readonly StyledProperty<double> RestitutionProperty =
        AvaloniaProperty.Register<HourglassVisual, double>(nameof(Restitution), 0.00);

    private readonly DispatcherTimer _renderTimer;
    private DateTime _lastRenderTickUtc;
    private readonly Random _rng = new(0x5C_52_59_01);

    private readonly List<Grain> _grains = new(1000);
    private readonly Dictionary<long, List<int>> _grid = new();
    private readonly SolidColorBrush?[] _sandBrushRamp = new SolidColorBrush?[18];

    private double _lastFrac = 1.0;
    private double _lastW;
    private double _lastH;
    private double _lastGrainRadiusScale = 1.0;
    private int _lastParticleCount = 1000;
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

    public double Gravity
    {
        get => GetValue(GravityProperty);
        set => SetValue(GravityProperty, value);
    }

    public double Damping
    {
        get => GetValue(DampingProperty);
        set => SetValue(DampingProperty, value);
    }

    public double JitterAccel
    {
        get => GetValue(JitterAccelProperty);
        set => SetValue(JitterAccelProperty, value);
    }

    public double SettleKeep
    {
        get => GetValue(SettleKeepProperty);
        set => SetValue(SettleKeepProperty, value);
    }

    public double SleepThreshold
    {
        get => GetValue(SleepThresholdProperty);
        set => SetValue(SleepThresholdProperty, value);
    }

    public int MaxReleasePerFrame
    {
        get => GetValue(MaxReleasePerFrameProperty);
        set => SetValue(MaxReleasePerFrameProperty, value);
    }

    public int ParticleCount
    {
        get => GetValue(ParticleCountProperty);
        set => SetValue(ParticleCountProperty, value);
    }

    public double GrainRadiusScale
    {
        get => GetValue(GrainRadiusScaleProperty);
        set => SetValue(GrainRadiusScaleProperty, value);
    }

    public double CollisionFriction
    {
        get => GetValue(CollisionFrictionProperty);
        set => SetValue(CollisionFrictionProperty, value);
    }

    public double CollisionStabilization
    {
        get => GetValue(CollisionStabilizationProperty);
        set => SetValue(CollisionStabilizationProperty, value);
    }

    public double Restitution
    {
        get => GetValue(RestitutionProperty);
        set => SetValue(RestitutionProperty, value);
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
        var gravity = Clamp(Gravity, 200.0, 8000.0);
        var damping = Clamp(Damping, 0.0, 0.40);
        var dt2 = dtSeconds * dtSeconds;

        for (var i = 0; i < _grains.Count; i++)
        {
            var g = _grains[i];

            // Unreleased grains still simulate/collide in the top chamber so the surface can collapse
            // and form a funnel as grains are removed.

            var vx = (g.X - g.PrevX) * (1.0 - damping);
            var vy = (g.Y - g.PrevY) * (1.0 - damping);

            g.PrevX = g.X;
            g.PrevY = g.Y;

            // Tiny horizontal noise as acceleration (scaled by dt^2) so it doesn't cause frame-to-frame shaking.
            // Unreleased grains should be very stable at the start (near-full), then gradually become dynamic
            // as grains are removed so a funnel can form.
            var inactiveActivity = Math.Clamp((1.0 - frac) / 0.18, 0.0, 1.0);
            var jitterScale = g.Active ? 1.0 : (0.02 + (0.18 * inactiveActivity));
            var jitterAx = (0.5 - _rng.NextDouble()) * Clamp(JitterAccel, 0.0, 1200.0) * jitterScale;

            var gravityScale = g.Active ? 1.0 : (0.02 + (0.78 * inactiveActivity));

            g.X += vx + (jitterAx * dt2);
            g.Y += vy + (gravity * gravityScale * dt2);

            _grains[i] = g;
        }

        // Constraint iterations.
        var iterations = _grains.Count > 2500 ? 14 : 12;
        for (var it = 0; it < iterations; it++)
        {
            ConstrainToHourglass(geom);
            SolveParticleCollisions();
        }

        // Settle pass: once grains are in the bottom chamber, strongly damp their velocity so the pile calms down.
        // This makes the sand feel heavier without needing complex friction modeling.
        var settleLine = geom.NeckY1 + (geom.GrainR * 2.0);
        for (var i = 0; i < _grains.Count; i++)
        {
            var g = _grains[i];
            if (!g.Active) continue;

            if (g.Y < settleLine)
            {
                continue;
            }

            var vx = g.X - g.PrevX;
            var vy = g.Y - g.PrevY;

            // If it's already moving slowly, put it to sleep.
            if ((Math.Abs(vx) + Math.Abs(vy)) < Clamp(SleepThreshold, 0.0, 10.0))
            {
                g.PrevX = g.X;
                g.PrevY = g.Y;
                _grains[i] = g;
                continue;
            }

            // Otherwise, damp strongly.
            var settleKeep = Clamp(SettleKeep, 0.0, 1.0);
            g.PrevX = g.X - (vx * settleKeep);
            g.PrevY = g.Y - (vy * settleKeep);
            _grains[i] = g;
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
        var grainScale = Clamp(GrainRadiusScale, 0.35, 2.50);
        var grainScaleChanged = Math.Abs(_lastGrainRadiusScale - grainScale) > 0.001;
        var desiredCount = Clamp(ParticleCount, 50, 8000);
        var countChanged = desiredCount != _lastParticleCount;

        if (_needsReseed || sizeChanged || grainScaleChanged || countChanged || (!IsRunning && fracIncreased))
        {
            _lastW = w;
            _lastH = h;
            _lastGrainRadiusScale = grainScale;
            _lastParticleCount = desiredCount;
            _needsReseed = false;
            _releaseCarry = 0;
            ReseedGrains(w, h, frac);
        }
    }

    private void ReseedGrains(double w, double h, double frac)
    {
        _grains.Clear();
        var geom = GetGeometry(w, h);

        var total = Clamp(ParticleCount, 50, 8000);
        var topCount = (int)Math.Round(total * frac);
        if (topCount < 0) topCount = 0;
        if (topCount > total) topCount = total;

        var r = geom.GrainR;

        // Populate top as a packed pile (inactive until released).
        FillPackedTop(geom, r, topCount);

        // Populate bottom as a packed pile (already released/active).
        FillPackedBottom(geom, r, total - topCount);


        RelaxAfterReseed(geom);
    }

    private void RelaxAfterReseed(HourglassGeom geom)
    {
        // Resolve any overlaps from packing/fallback sampling.
        for (var it = 0; it < 40; it++)
        {
            ConstrainToHourglass(geom);
            SolveParticleCollisions();
        }

        // Freeze velocities so we don't start with correction-as-velocity.
        for (var i = 0; i < _grains.Count; i++)
        {
            var g = _grains[i];
            g.PrevX = g.X;
            g.PrevY = g.Y;
            _grains[i] = g;
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

        var total = Clamp(ParticleCount, 50, 8000);
        var desired = (deltaFrac * total) + _releaseCarry;
        var toRelease = (int)Math.Floor(desired);
        _releaseCarry = desired - toRelease;

        if (toRelease <= 0)
        {
            return;
        }

        // Avoid huge bursts on lag spikes.
        var cap = Clamp(MaxReleasePerFrame, 1, 100);
        toRelease = Math.Min(toRelease, cap);
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
        // Only allow one grain to be in the neck entrance at a time.
        var neckHalfForCenter = Math.Max(0.05, geom.FlowHalfW - geom.GrainR);
        var yGateTop = geom.NeckY0 + (geom.GrainR * 0.25);
        var yGateBottom = geom.NeckY0 + (geom.GrainR * 2.4);
        for (var i = 0; i < _grains.Count; i++)
        {
            var g0 = _grains[i];
            if (!g0.Active) continue;
            if (g0.Y < yGateTop || g0.Y > yGateBottom) continue;
            if (Math.Abs(g0.X - geom.Cx) <= (neckHalfForCenter + (geom.GrainR * 0.20)))
            {
                return false;
            }
        }

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
        // Tighten to a single-stream corridor inside the neck.
        var x = geom.Cx + (geom.Rng.NextDouble() - 0.5) * (Math.Max(0.10, neckHalfForCenter) * 0.90);
        var y = geom.NeckY0 + r + (geom.Rng.NextDouble() * r * 0.6);

        var grain = _grains[bestIndex];
        grain.Active = true;
        grain.X = x;
        grain.Y = y;

        // Give it a stable downward initial velocity (not too fast so it doesn't "explode" the pile).
        var vy0 = 120.0;
        var vx0 = (geom.Rng.NextDouble() - 0.5) * 10.0;
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
                // Unreleased grains simulate/collide, but are not allowed to enter the neck.
                if (p.Y < topMinY) p.Y = topMinY;
                if (p.Y > topMaxY) p.Y = topMaxY;
                var tt = (p.Y - geom.TopY0) / (geom.TopY1 - geom.TopY0);
                if (tt < 0) tt = 0;
                if (tt > 1) tt = 1;
                var halfT = Lerp(geom.TopHalfW - r, geom.NeckHalfW - r, tt);
                var minXT = geom.Cx - halfT;
                var maxXT = geom.Cx + halfT;
                if (p.X < minXT) { p.X = minXT; p.PrevX = p.X; }
                if (p.X > maxXT) { p.X = maxXT; p.PrevX = p.X; }
                if (p.Y < topMinY) { p.Y = topMinY; p.PrevY = p.Y; }
                if (p.Y > topMaxY) { p.Y = topMaxY; p.PrevY = p.Y; }
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
                // Keep the falling stream narrow (single-file look).
                var half = Math.Max(0.05, geom.FlowHalfW - r);
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

        var friction = Clamp(CollisionFriction, 0.0, 1.0);
        var stabilize = Clamp(CollisionStabilization, 0.0, 1.0);
        var restitution = Clamp(Restitution, 0.0, 0.40);

        for (var i = 0; i < _grains.Count; i++)
        {
            var p = _grains[i];
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
                        var dx = b.X - a.X;
                        var dy = b.Y - a.Y;
                        // Slight padding helps avoid visible overlap with square rendering.
                        var minDist = (a.R + b.R) * 1.02;
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

                        // Key stability trick: move previous positions with the correction so we don't inject energy.
                        // Without this, constraint/collision corrections turn into "velocity" next frame and everything bounces.
                        if (stabilize > 0)
                        {
                            var sx = nx * push * stabilize;
                            var sy = ny * push * stabilize;
                            a.PrevX -= sx;
                            a.PrevY -= sy;
                            b.PrevX += sx;
                            b.PrevY += sy;
                        }

                        // Approximate inelastic collision + sand friction by damping the relative motion between grains.
                        // In Verlet, velocity ~ (pos - prev), so we adjust Prev to change velocity.
                        var avx = a.X - a.PrevX;
                        var avy = a.Y - a.PrevY;
                        var bvx = b.X - b.PrevX;
                        var bvy = b.Y - b.PrevY;

                        var rvx = bvx - avx;
                        var rvy = bvy - avy;

                        // Tangent (perpendicular to normal)
                        var tx = -ny;
                        var ty = nx;

                        var vn = (rvx * nx) + (rvy * ny);
                        var vt = (rvx * tx) + (rvy * ty);

                        // Restitution preserves some normal motion; friction kills tangential sliding.
                        var vnNew = vn * restitution;
                        var vtNew = vt * (1.0 - friction);

                        var rvxNew = (vnNew * nx) + (vtNew * tx);
                        var rvyNew = (vnNew * ny) + (vtNew * ty);

                        var dvx = rvxNew - rvx;
                        var dvy = rvyNew - rvy;

                        // Split adjustment across the pair.
                        avx -= dvx * 0.5;
                        avy -= dvy * 0.5;
                        bvx += dvx * 0.5;
                        bvy += dvy * 0.5;

                        a.PrevX = a.X - avx;
                        a.PrevY = a.Y - avy;
                        b.PrevX = b.X - bvx;
                        b.PrevY = b.Y - bvy;

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
        public readonly double FlowHalfW;
        public readonly double BotHalfW;
        public readonly double GrainR;
        public readonly Random Rng;

        public HourglassGeom(double w, double h, Random rng, double grainRadiusScale)
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

            GrainR = Math.Clamp(Math.Min(w, h) * 0.0065 * grainRadiusScale, 1.1, 7.0);

            var topW = w - (M * 2);
            // Neck is sized relative to the grain radius to enforce a one-grain-wide chute.
            var neckW = Math.Max(GrainR * 2.6, w * 0.035);
            TopHalfW = topW / 2.0;
            NeckHalfW = neckW / 2.0;
            FlowHalfW = Math.Min(NeckHalfW, GrainR * 1.15);
            BotHalfW = TopHalfW;
        }
    }

    private HourglassGeom GetGeometry(double w, double h)
        => new(w, h, _rng, Clamp(GrainRadiusScale, 0.35, 2.50));

    private static double Clamp(double value, double min, double max)
        => value < min ? min : (value > max ? max : value);

    private static int Clamp(int value, int min, int max)
        => value < min ? min : (value > max ? max : value);

    private struct Grain
    {
        public double X;
        public double Y;
        public double PrevX;
        public double PrevY;
        public double R;
        public bool Active;
        public sbyte ShadeOffset;

        public Grain(double x, double y, double r)
        {
            X = x;
            Y = y;
            PrevX = x;
            PrevY = y;
            R = r;
            Active = true;
            ShadeOffset = 0;
        }
    }

    private void FillPackedTop(HourglassGeom geom, double r, int count)
    {
        if (count <= 0)
        {
            return;
        }

        // Pack starting near the neck (bottom of top chamber) and build upward.
        var spacingX = r * 2.02;
        var spacingY = r * 2.02;
        var y = geom.TopY1 - r;

        while (_grains.Count < count && y > geom.TopY0 + r)
        {
            var t = (y - geom.TopY0) / (geom.TopY1 - geom.TopY0);
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            var half = Lerp(geom.TopHalfW - r, geom.NeckHalfW - r, t);

            for (var x = geom.Cx - half; x <= geom.Cx + half && _grains.Count < count; x += spacingX)
            {
                var g = new Grain(x, y, r) { Active = false, ShadeOffset = (sbyte)geom.Rng.Next(-2, 3) };
                _grains.Add(g);
            }

            y -= spacingY;
        }

        // If we couldn't pack enough (very small sizes), fill the rest randomly.
        while (_grains.Count < count)
        {
            var p = RandomPointInTop(geom, r);
            var g = new Grain(p.X, p.Y, r) { Active = false, ShadeOffset = (sbyte)geom.Rng.Next(-2, 3) };
            _grains.Add(g);
        }
    }

    private void FillPackedBottom(HourglassGeom geom, double r, int count)
    {
        if (count <= 0)
        {
            return;
        }

        var spacingX = r * 2.02;
        var spacingY = r * 2.02;
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
                var g = new Grain(x, y, r) { Active = true, ShadeOffset = (sbyte)geom.Rng.Next(-2, 3) };
                _grains.Add(g);
                filled++;
            }

            y -= spacingY;
        }

        while (filled < count)
        {
            var p = RandomPointInBottom(geom, r);
            var g = new Grain(p.X, p.Y, r) { Active = true, ShadeOffset = (sbyte)geom.Rng.Next(-2, 3) };
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

        var geom = GetGeometry(w, h);

        // Layout.
        var cx = geom.Cx;
        var m = geom.M;
        var neckH = geom.NeckH;
        var chamberH = geom.ChamberH;

        var topY0 = geom.TopY0;
        var topY1 = geom.TopY1;
        var neckY0 = geom.NeckY0;
        var neckY1 = geom.NeckY1;
        var botY0 = geom.BotY0;
        var botY1 = geom.BotY1;

        var topW = w - (m * 2);
        var botW = topW;
        var neckW = Math.Max(4, geom.NeckHalfW * 2.0);
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
            },
        };
        context.DrawGeometry(glass, null, outline);

        // Sand grains.
        using (context.PushGeometryClip(outline))
        {
            for (var i = 0; i < _grains.Count; i++)
            {
                var p = _grains[i];

                // Color varies slightly by height + per-grain offset to feel more like real sand.
                var t = Math.Clamp((p.Y - topY0) / (botY1 - topY0), 0, 1);
                var baseIdx = (int)Math.Round(t * (_sandBrushRamp.Length - 1));
                if (baseIdx < 0) baseIdx = 0;
                if (baseIdx >= _sandBrushRamp.Length) baseIdx = _sandBrushRamp.Length - 1;
                var idx = baseIdx + p.ShadeOffset;
                if (idx < 0) idx = 0;
                if (idx >= _sandBrushRamp.Length) idx = _sandBrushRamp.Length - 1;

                var brush = _sandBrushRamp[idx] ?? GetSandBrush(idx / (double)(_sandBrushRamp.Length - 1));

                // Square grains (pixel/sand look)
                var s = Math.Max(0.8, (p.R * 2.0) - 0.75);
                var hs = s * 0.5;
                context.FillRectangle(brush, new Rect(p.X - hs, p.Y - hs, s, s));
            }
        }

        // Specular highlights (fantasy glass sheen).
        var highlight = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), thickness: Math.Max(1, Math.Min(w, h) * 0.01));
        context.DrawLine(highlight, new Point(cx - topW * 0.24, topY0 + chamberH * 0.08), new Point(cx - neckW * 0.55, neckY0 + neckH * 0.15));
        context.DrawLine(highlight, new Point(cx + topW * 0.26, topY0 + chamberH * 0.12), new Point(cx + neckW * 0.62, neckY0 + neckH * 0.25));
    }
}
