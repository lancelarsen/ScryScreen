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

    private readonly List<SandParticle> _particles = new();
    private double _spawnAccumulator;

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
        _particles.Clear();
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

        UpdateParticles(dt.TotalSeconds);
        InvalidateVisual();
    }

    private void UpdateParticles(double dtSeconds)
    {
        // Only emit particles while sand is flowing.
        var frac = FractionRemaining;
        if (double.IsNaN(frac) || double.IsInfinity(frac)) frac = 0;
        if (frac < 0) frac = 0;
        if (frac > 1) frac = 1;

        var shouldFlow = IsRunning && frac > 0 && frac < 1;
        if (!shouldFlow)
        {
            _particles.Clear();
            _spawnAccumulator = 0;
            return;
        }

        var w = Bounds.Width;
        var h = Bounds.Height;

        // Spawn rate scales with control size.
        var areaScale = Math.Clamp((w * h) / (420.0 * 640.0), 0.4, 2.5);
        var spawnPerSecond = 220.0 * areaScale;
        _spawnAccumulator += spawnPerSecond * dtSeconds;

        var maxParticles = (int)(900 * areaScale);
        while (_spawnAccumulator >= 1 && _particles.Count < maxParticles)
        {
            _spawnAccumulator -= 1;
            _particles.Add(SandParticle.Create(_rng, w, h, areaScale));
        }

        // Integrate.
        var gravity = 1600.0 * areaScale;
        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Vy += gravity * dtSeconds;
            p.X += p.Vx * dtSeconds;
            p.Y += p.Vy * dtSeconds;
            p.Life -= dtSeconds;
            _particles[i] = p;

            if (p.Life <= 0)
            {
                _particles.RemoveAt(i);
            }
        }
    }

    private struct SandParticle
    {
        public double X;
        public double Y;
        public double Vx;
        public double Vy;
        public double Life;
        public double Size;

        public static SandParticle Create(Random rng, double w, double h, double areaScale)
        {
            // Spawn in pixel coordinates near the neck.
            var x = (w * 0.5) + (rng.NextDouble() - 0.5) * (w * 0.04);
            var y = (h * 0.50) + (rng.NextDouble() - 0.5) * (h * 0.01);
            var vx = (rng.NextDouble() - 0.5) * 90 * areaScale;
            var vy = rng.NextDouble() * 60 * areaScale;

            return new SandParticle
            {
                X = x,
                Y = y,
                Vx = vx,
                Vy = vy,
                Life = 0.45 + rng.NextDouble() * 0.25,
                Size = 0.8 + rng.NextDouble() * 1.4,
            };
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

        var frac = FractionRemaining;
        if (double.IsNaN(frac) || double.IsInfinity(frac)) frac = 0;
        if (frac < 0) frac = 0;
        if (frac > 1) frac = 1;

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

        // Sand brush.
        var sandBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.FromArgb(230, 244, 210, 120), 0),
                new GradientStop(Color.FromArgb(230, 232, 160, 72), 1),
            }
        };

        // Top chamber clip (curved trapezoid).
        var topClip = new StreamGeometry();
        using (var g = topClip.Open())
        {
            var tl = new Point(cx - topW / 2 + m * 0.20, topY0 + m * 0.15);
            var tr = new Point(cx + topW / 2 - m * 0.20, topY0 + m * 0.15);
            var br = new Point(cx + neckW / 2, topY1);
            var bl = new Point(cx - neckW / 2, topY1);

            g.BeginFigure(tl, isFilled: true);
            g.LineTo(tr);
            g.CubicBezierTo(new Point(tr.X, tr.Y + curve * 0.35), new Point(br.X + curve * 0.15, br.Y - curve * 0.35), br);
            g.LineTo(bl);
            g.CubicBezierTo(new Point(bl.X - curve * 0.15, bl.Y - curve * 0.35), new Point(tl.X, tl.Y + curve * 0.35), tl);
            g.EndFigure(isClosed: true);
        }

        // Bottom chamber clip (curved trapezoid).
        var botClip = new StreamGeometry();
        using (var g = botClip.Open())
        {
            var tl = new Point(cx - neckW / 2, botY0);
            var tr = new Point(cx + neckW / 2, botY0);
            var br = new Point(cx + botW / 2 - m * 0.20, botY1 - m * 0.15);
            var bl = new Point(cx - botW / 2 + m * 0.20, botY1 - m * 0.15);

            g.BeginFigure(tl, isFilled: true);
            g.LineTo(tr);
            g.CubicBezierTo(new Point(tr.X + curve * 0.15, tr.Y + curve * 0.35), new Point(br.X, br.Y - curve * 0.35), br);
            g.LineTo(bl);
            g.CubicBezierTo(new Point(bl.X, bl.Y - curve * 0.35), new Point(tl.X - curve * 0.15, tl.Y + curve * 0.35), tl);
            g.EndFigure(isClosed: true);
        }

        // Top sand: remaining (flat-ish with slight curve).
        var topFillHeight = chamberH * frac;
        using (context.PushGeometryClip(topClip))
        {
            var y = topY1 - topFillHeight;
            var mound = new StreamGeometry();
            using (var g = mound.Open())
            {
                var left = new Point(m, topY1);
                var right = new Point(w - m, topY1);
                var topLeft = new Point(m, y);
                var topRight = new Point(w - m, y);
                var bulge = Math.Min(18, topFillHeight * 0.35);

                g.BeginFigure(left, isFilled: true);
                g.LineTo(right);
                g.LineTo(topRight);
                g.CubicBezierTo(new Point(cx + topW * 0.10, y + bulge), new Point(cx - topW * 0.10, y + bulge), topLeft);
                g.EndFigure(isClosed: true);
            }
            context.DrawGeometry(sandBrush, null, mound);
        }

        // Bottom sand: accumulated (mounded). Leave a tiny "dead space" for realism when emptying completes.
        const double bottomDeadSpaceFrac = 0.05;
        var botFillFrac = 1 - frac;
        var botMaxFillFrac = 1 - bottomDeadSpaceFrac;
        if (botFillFrac > botMaxFillFrac) botFillFrac = botMaxFillFrac;
        var botFillHeight = chamberH * botFillFrac;
        using (context.PushGeometryClip(botClip))
        {
            var y = botY1 - botFillHeight;
            var mound = new StreamGeometry();
            using (var g = mound.Open())
            {
                var left = new Point(m, botY1);
                var right = new Point(w - m, botY1);
                var topLeft = new Point(m, y);
                var topRight = new Point(w - m, y);
                var bulge = Math.Min(28, botFillHeight * 0.55);

                g.BeginFigure(left, isFilled: true);
                g.LineTo(right);
                g.LineTo(topRight);
                g.CubicBezierTo(new Point(cx + botW * 0.14, y + bulge), new Point(cx - botW * 0.14, y + bulge), topLeft);
                g.EndFigure(isClosed: true);
            }
            context.DrawGeometry(sandBrush, null, mound);
        }

        // Falling sand: tapered stream + particles.
        var shouldFlow = IsRunning && frac > 0 && frac < 1;
        if (shouldFlow)
        {
            var stream = new StreamGeometry();
            using (var g = stream.Open())
            {
                var x0 = cx;
                var y0 = neckY0 + 2;
                var y1 = neckY1 - 2;
                var half = Math.Max(1.2, w * 0.010);
                g.BeginFigure(new Point(x0 - half, y0), isFilled: true);
                g.LineTo(new Point(x0 + half, y0));
                g.LineTo(new Point(x0 + half * 0.55, y1));
                g.LineTo(new Point(x0 - half * 0.55, y1));
                g.EndFigure(isClosed: true);
            }

            var streamBrush = new SolidColorBrush(Color.FromArgb(170, 232, 160, 72));
            context.DrawGeometry(streamBrush, null, stream);

            // Particles in normalized coords: map into the neck/bottom region.
            var particleBrush = new SolidColorBrush(Color.FromArgb(210, 244, 210, 120));
            foreach (var p in _particles)
            {
                var px = p.X;
                var py = p.Y;
                // Constrain to the hourglass area visually.
                if (py < neckY0 - 8 || py > botY1 + 10) continue;
                context.DrawEllipse(particleBrush, null, new Point(px, py), p.Size, p.Size);
            }
        }

        // Specular highlights (fantasy glass sheen).
        var highlight = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), thickness: Math.Max(1, Math.Min(w, h) * 0.01));
        context.DrawLine(highlight, new Point(cx - topW * 0.24, topY0 + chamberH * 0.08), new Point(cx - neckW * 0.55, neckY0 + neckH * 0.15));
        context.DrawLine(highlight, new Point(cx + topW * 0.26, topY0 + chamberH * 0.12), new Point(cx + neckW * 0.62, neckY0 + neckH * 0.25));
    }
}
