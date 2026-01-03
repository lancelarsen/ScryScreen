using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ScryScreen.App.Controls;

public sealed class OverlayEffectsLayer : Control
{
    public static readonly StyledProperty<bool> RainEnabledProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, bool>(nameof(RainEnabled));

    public static readonly StyledProperty<double> RainIntensityProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, double>(nameof(RainIntensity), defaultValue: 0.5);

    public static readonly StyledProperty<bool> SnowEnabledProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, bool>(nameof(SnowEnabled));

    public static readonly StyledProperty<double> SnowIntensityProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, double>(nameof(SnowIntensity), defaultValue: 0.5);

    public static readonly StyledProperty<bool> SandEnabledProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, bool>(nameof(SandEnabled));

    public static readonly StyledProperty<double> SandIntensityProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, double>(nameof(SandIntensity), defaultValue: 0.5);

    public static readonly StyledProperty<bool> FogEnabledProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, bool>(nameof(FogEnabled));

    public static readonly StyledProperty<double> FogIntensityProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, double>(nameof(FogIntensity), defaultValue: 0.5);

    public static readonly StyledProperty<bool> SmokeEnabledProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, bool>(nameof(SmokeEnabled));

    public static readonly StyledProperty<double> SmokeIntensityProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, double>(nameof(SmokeIntensity), defaultValue: 0.5);

    public static readonly StyledProperty<bool> LightningEnabledProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, bool>(nameof(LightningEnabled));

    public static readonly StyledProperty<double> LightningIntensityProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, double>(nameof(LightningIntensity), defaultValue: 0.35);

    public bool RainEnabled { get => GetValue(RainEnabledProperty); set => SetValue(RainEnabledProperty, value); }
    public double RainIntensity { get => GetValue(RainIntensityProperty); set => SetValue(RainIntensityProperty, value); }

    public bool SnowEnabled { get => GetValue(SnowEnabledProperty); set => SetValue(SnowEnabledProperty, value); }
    public double SnowIntensity { get => GetValue(SnowIntensityProperty); set => SetValue(SnowIntensityProperty, value); }

    public bool SandEnabled { get => GetValue(SandEnabledProperty); set => SetValue(SandEnabledProperty, value); }
    public double SandIntensity { get => GetValue(SandIntensityProperty); set => SetValue(SandIntensityProperty, value); }

    public bool FogEnabled { get => GetValue(FogEnabledProperty); set => SetValue(FogEnabledProperty, value); }
    public double FogIntensity { get => GetValue(FogIntensityProperty); set => SetValue(FogIntensityProperty, value); }

    public bool SmokeEnabled { get => GetValue(SmokeEnabledProperty); set => SetValue(SmokeEnabledProperty, value); }
    public double SmokeIntensity { get => GetValue(SmokeIntensityProperty); set => SetValue(SmokeIntensityProperty, value); }

    public bool LightningEnabled { get => GetValue(LightningEnabledProperty); set => SetValue(LightningEnabledProperty, value); }
    public double LightningIntensity { get => GetValue(LightningIntensityProperty); set => SetValue(LightningIntensityProperty, value); }

    private readonly DispatcherTimer _timer;
    private readonly Random _rng = new();

    private readonly List<RainDrop> _rain = new();
    private readonly List<SnowFlake> _snow = new();
    private readonly List<SandGrain> _sand = new();
    private readonly List<HazePuff> _fog = new();
    private readonly List<HazePuff> _smoke = new();

    private readonly Pen?[] _rainPens = new Pen?[256];
    private readonly SolidColorBrush?[] _snowBrushes = new SolidColorBrush?[256];
    private readonly Pen?[] _sandPens = new Pen?[256];

    private DateTime _lastTickUtc = DateTime.UtcNow;
    private double _timeSeconds;

    private double _lightningFlash;
    private List<Point>? _lightningPath;

    public OverlayEffectsLayer()
    {
        IsHitTestVisible = false;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, (_, _) => Tick());
        this.AttachedToVisualTree += (_, _) => { _lastTickUtc = DateTime.UtcNow; _timer.Start(); };
        this.DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    private static double DensityCurveCubic(double intensity)
    {
        // Higher density at the high end without over-inflating mid/low.
        // Range ~0.15..3.0 for intensity 0..1.
        var t = Clamp01(intensity);
        return 0.15 + (2.85 * t * t * t);
    }

    private Pen GetCachedPen(Pen?[] cache, byte a, byte r, byte g, byte b, double thickness)
    {
        var pen = cache[a];
        if (pen is not null)
        {
            return pen;
        }

        pen = new Pen(new SolidColorBrush(Color.FromArgb(a, r, g, b)), thickness);
        cache[a] = pen;
        return pen;
    }

    private SolidColorBrush GetCachedBrush(SolidColorBrush?[] cache, byte a, byte r, byte g, byte b)
    {
        var brush = cache[a];
        if (brush is not null)
        {
            return brush;
        }

        brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        cache[a] = brush;
        return brush;
    }

    private bool AnyEnabled =>
        RainEnabled || SnowEnabled || SandEnabled || FogEnabled || SmokeEnabled || LightningEnabled;

    private void Tick()
    {
        if (!IsVisible)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var dt = (now - _lastTickUtc).TotalSeconds;
        if (dt <= 0)
        {
            dt = 1.0 / 60.0;
        }
        _lastTickUtc = now;
        _timeSeconds += dt;

        if (!AnyEnabled)
        {
            // Decay flash even if toggled off.
            _lightningFlash = Math.Max(0, _lightningFlash - dt * 6);
            _lightningPath = null;

            if (_rain.Count > 0 || _snow.Count > 0 || _fog.Count > 0 || _smoke.Count > 0)
            {
                _rain.Clear();
                _snow.Clear();
                _sand.Clear();
                _fog.Clear();
                _smoke.Clear();
                InvalidateVisual();
            }
            return;
        }

        var w = Math.Max(1, Bounds.Width);
        var h = Math.Max(1, Bounds.Height);

        UpdateRain(dt, w, h);
        UpdateSnow(dt, w, h);
        UpdateSand(dt, w, h);
        UpdateFog(dt, w, h);
        UpdateSmoke(dt, w, h);
        UpdateLightning(dt, w, h);

        InvalidateVisual();
    }

    private void UpdateRain(double dt, double w, double h)
    {
        var intensity = Clamp01(RainIntensity);
        if (!RainEnabled || intensity <= 0)
        {
            _rain.Clear();
            return;
        }

        // 10x baseline * 3x requested boost.
        var target = (int)(3600 * DensityCurveCubic(intensity));
        while (_rain.Count < target)
        {
            _rain.Add(new RainDrop(
                x: _rng.NextDouble() * w,
                y: _rng.NextDouble() * h,
                vy: 900 + (_rng.NextDouble() * 700),
                len: 10 + (_rng.NextDouble() * 18),
                alpha: 0.10 + (_rng.NextDouble() * 0.18)));
        }

        for (var i = _rain.Count - 1; i >= 0; i--)
        {
            var d = _rain[i];
            d.Y += d.Vy * dt;
            if (d.Y - d.Len > h)
            {
                d.Y = -_rng.NextDouble() * 80;
                d.X = _rng.NextDouble() * w;
            }
            _rain[i] = d;
        }

        // Keep list near target.
        if (_rain.Count > target)
        {
            _rain.RemoveRange(target, _rain.Count - target);
        }
    }

    private void UpdateSnow(double dt, double w, double h)
    {
        var intensity = Clamp01(SnowIntensity);
        if (!SnowEnabled || intensity <= 0)
        {
            _snow.Clear();
            return;
        }

        // 10x baseline * 3x requested boost.
        var target = (int)(2700 * DensityCurveCubic(intensity));
        while (_snow.Count < target)
        {
            _snow.Add(new SnowFlake(
                x: _rng.NextDouble() * w,
                y: _rng.NextDouble() * h,
                vx: -25 + _rng.NextDouble() * 50,
                vy: 35 + _rng.NextDouble() * 80,
                r: 1.0 + _rng.NextDouble() * 2.2,
                alpha: 0.10 + _rng.NextDouble() * 0.18));
        }

        for (var i = _snow.Count - 1; i >= 0; i--)
        {
            var f = _snow[i];
            f.X += f.Vx * dt;
            f.Y += f.Vy * dt;

            // slight wobble
            f.X += Math.Sin((f.Y / 25.0) + i) * dt * 12;

            if (f.Y - f.R > h)
            {
                f.Y = -_rng.NextDouble() * 80;
                f.X = _rng.NextDouble() * w;
            }

            if (f.X < -20) f.X = w + 20;
            if (f.X > w + 20) f.X = -20;

            _snow[i] = f;
        }

        if (_snow.Count > target)
        {
            _snow.RemoveRange(target, _snow.Count - target);
        }
    }

    private void UpdateSand(double dt, double w, double h)
    {
        var intensity = Clamp01(SandIntensity);
        if (!SandEnabled || intensity <= 0)
        {
            _sand.Clear();
            return;
        }

        // A sand storm is a lot of small, fast streaks.
        // Keep it dense but still performant.
        // 10x baseline * 3x requested boost.
        var target = (int)(4200 * (0.20 + (2.20 * intensity * intensity)));
        while (_sand.Count < target)
        {
            var speed = 420 + (_rng.NextDouble() * 780);
            // Mostly right-to-left with a slight downward component.
            var vx = -(speed * (0.85 + _rng.NextDouble() * 0.25));
            var vy = speed * (0.05 + _rng.NextDouble() * 0.20);

            _sand.Add(new SandGrain(
                x: _rng.NextDouble() * w,
                y: _rng.NextDouble() * h,
                vx: vx,
                vy: vy,
                len: 6 + (_rng.NextDouble() * 16),
                alpha: 0.06 + (_rng.NextDouble() * 0.16)));
        }

        for (var i = _sand.Count - 1; i >= 0; i--)
        {
            var g = _sand[i];
            g.X += g.Vx * dt;
            g.Y += g.Vy * dt;

            // A little gusty wobble.
            g.Y += Math.Sin((g.X / 60.0) + i) * dt * (40 * intensity);

            if (g.X + g.Len < -80 || g.Y - g.Len > h + 80)
            {
                // Respawn off the right/top edges.
                g.X = w + (_rng.NextDouble() * 120);
                g.Y = _rng.NextDouble() * (h + 120) - 60;
            }

            _sand[i] = g;
        }

        if (_sand.Count > target)
        {
            _sand.RemoveRange(target, _sand.Count - target);
        }
    }

    private void UpdateFog(double dt, double w, double h)
    {
        var intensity = Clamp01(FogIntensity);
        if (!FogEnabled || intensity <= 0)
        {
            _fog.Clear();
            return;
        }

        // 3x volume requested.
        var target = 18 + (int)(30 * intensity);
        while (_fog.Count < target)
        {
            _fog.Add(HazePuff.Create(_rng, w, h, upward: false, dark: false));
        }

        for (var i = _fog.Count - 1; i >= 0; i--)
        {
            var p = _fog[i];
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;

            if (p.X + p.R < -100) p.X = w + p.R + 100;
            if (p.X - p.R > w + 100) p.X = -p.R - 100;

            // wrap vertical gently
            if (p.Y + p.R < -120) p.Y = h + p.R + 120;
            if (p.Y - p.R > h + 120) p.Y = -p.R - 120;

            _fog[i] = p;
        }

        if (_fog.Count > target)
        {
            _fog.RemoveRange(target, _fog.Count - target);
        }
    }

    private void UpdateSmoke(double dt, double w, double h)
    {
        var intensity = Clamp01(SmokeIntensity);
        if (!SmokeEnabled || intensity <= 0)
        {
            _smoke.Clear();
            return;
        }

        // 3x volume requested.
        var target = 15 + (int)(27 * intensity);
        while (_smoke.Count < target)
        {
            _smoke.Add(HazePuff.Create(_rng, w, h, upward: true, dark: true));
        }

        for (var i = _smoke.Count - 1; i >= 0; i--)
        {
            var p = _smoke[i];
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            p.R += dt * (6 + _rng.NextDouble() * 8);
            p.Alpha = Math.Max(0, p.Alpha - dt * (0.010 + (1 - intensity) * 0.020));

            if (p.Alpha <= 0 || p.Y + p.R < -200)
            {
                _smoke[i] = HazePuff.Create(_rng, w, h, upward: true, dark: true);
                continue;
            }

            if (p.X + p.R < -160) p.X = w + p.R + 160;
            if (p.X - p.R > w + 160) p.X = -p.R - 160;

            _smoke[i] = p;
        }

        if (_smoke.Count > target)
        {
            _smoke.RemoveRange(target, _smoke.Count - target);
        }
    }

    private void UpdateLightning(double dt, double w, double h)
    {
        var intensity = Clamp01(LightningIntensity);
        if (!LightningEnabled || intensity <= 0)
        {
            _lightningFlash = Math.Max(0, _lightningFlash - dt * 6);
            _lightningPath = null;
            return;
        }

        // Flash decays quickly.
        _lightningFlash = Math.Max(0, _lightningFlash - dt * 6);

        // Trigger probability based on intensity.
        // 5x baseline * 3x requested boost.
        var flashesPerSecond = (0.06 + (0.25 * intensity)) * 15.0;
        if (_lightningFlash <= 0 && _rng.NextDouble() < flashesPerSecond * dt)
        {
            _lightningFlash = 1.0;
            _lightningPath = BuildLightningPath(w, h);
        }
    }

    private List<Point> BuildLightningPath(double w, double h)
    {
        var points = new List<Point>();
        var x = _rng.NextDouble() * w;
        var y = 0.0;
        points.Add(new Point(x, y));

        var segments = 10 + _rng.Next(10);
        var segH = h / segments;

        for (var i = 0; i < segments; i++)
        {
            x += (-w * 0.08) + (_rng.NextDouble() * w * 0.16);
            x = Math.Clamp(x, 0, w);
            y += segH;
            points.Add(new Point(x, y));
        }

        return points;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!AnyEnabled && _lightningFlash <= 0)
        {
            return;
        }

        var w = Math.Max(1, Bounds.Width);
        var h = Math.Max(1, Bounds.Height);

        // Fog/smoke base wash first.
        if (FogEnabled)
        {
            var intensity = Clamp01(FogIntensity);
            var a = (byte)(intensity * 55);
            if (a > 0)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(a, 160, 190, 220)), null, new Rect(0, 0, w, h));
            }

            // Flowing fog: layered wavy bands (no obvious circles).
            var bands = 3 + (int)(intensity * 6);
            for (var i = 0; i < bands; i++)
            {
                var t = _timeSeconds * (0.06 + (i * 0.03));
                var bandHeight = h * (0.18 + (i % 3) * 0.06);
                var y = ((Math.Sin(t + i) * 0.5) + 0.5) * (h - bandHeight);
                var xDrift = (Math.Sin((t * 1.7) + (i * 2.2)) * 0.5 + 0.5) * (w * 0.10);

                var alpha = (byte)Math.Clamp((18 + (intensity * 38)) - (i * 2), 0, 80);
                if (alpha == 0)
                {
                    continue;
                }

                var brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop(Color.FromArgb(0, 205, 225, 245), 0.0),
                        new GradientStop(Color.FromArgb(alpha, 205, 225, 245), 0.50),
                        new GradientStop(Color.FromArgb(0, 205, 225, 245), 1.0),
                    }
                };

                context.DrawRectangle(brush, null, new Rect(-xDrift, y, w + (xDrift * 2), bandHeight));
            }
        }

        if (SmokeEnabled)
        {
            var intensity = Clamp01(SmokeIntensity);
            var a = (byte)(intensity * 45);
            if (a > 0)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(a, 30, 35, 45)), null, new Rect(0, 0, w, h));
            }

            // Flowing smoke: vertical-ish wisps that drift upward.
            var wisps = 3 + (int)(intensity * 6);
            for (var i = 0; i < wisps; i++)
            {
                var t = _timeSeconds * (0.08 + (i * 0.04));
                var wispWidth = w * (0.10 + ((i % 3) * 0.04));
                var x = ((Math.Sin((t * 0.9) + i) * 0.5) + 0.5) * (w - wispWidth);
                var yDrift = (Math.Sin((t * 1.4) + (i * 1.7)) * 0.5 + 0.5) * (h * 0.10);

                var alpha = (byte)Math.Clamp((16 + (intensity * 45)) - (i * 2), 0, 95);
                if (alpha == 0)
                {
                    continue;
                }

                var brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0.0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1.0, 0.5, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop(Color.FromArgb(0, 55, 60, 70), 0.0),
                        new GradientStop(Color.FromArgb(alpha, 55, 60, 70), 0.50),
                        new GradientStop(Color.FromArgb(0, 55, 60, 70), 1.0),
                    }
                };

                context.DrawRectangle(brush, null, new Rect(x, -yDrift, wispWidth, h + (yDrift * 2)));
            }
        }

        if (SandEnabled)
        {
            // A subtle warm wash + streaks.
            var a = (byte)(Clamp01(SandIntensity) * 22);
            if (a > 0)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(a, 120, 95, 55)), null, new Rect(0, 0, w, h));
            }

            foreach (var g in _sand)
            {
                var alpha = (byte)Math.Clamp(g.Alpha * 255, 0, 255);
                if (alpha == 0)
                {
                    continue;
                }

                var pen = GetCachedPen(_sandPens, alpha, 210, 185, 120, thickness: 1);
                context.DrawLine(pen, new Point(g.X, g.Y), new Point(g.X + (g.Len * 0.9), g.Y + (g.Len * 0.18)));
            }
        }

        if (RainEnabled)
        {
            foreach (var d in _rain)
            {
                var alpha = (byte)Math.Clamp(d.Alpha * 255, 0, 255);
                if (alpha == 0)
                {
                    continue;
                }

                var pen = GetCachedPen(_rainPens, alpha, 190, 220, 255, thickness: 1);
                context.DrawLine(pen, new Point(d.X, d.Y), new Point(d.X, d.Y + d.Len));
            }
        }

        if (SnowEnabled)
        {
            foreach (var f in _snow)
            {
                var alpha = (byte)Math.Clamp(f.Alpha * 255, 0, 255);
                if (alpha == 0)
                {
                    continue;
                }

                var brush = GetCachedBrush(_snowBrushes, alpha, 235, 245, 255);
                context.DrawEllipse(brush, null, new Point(f.X, f.Y), f.R, f.R);
            }
        }

        if (_lightningFlash > 0 && LightningEnabled)
        {
            var intensity = Clamp01(LightningIntensity);
            var flashAlpha = (byte)(Math.Clamp(_lightningFlash * intensity * 160, 0, 200));
            if (flashAlpha > 0)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(flashAlpha, 255, 255, 255)), null, new Rect(0, 0, w, h));
            }

            if (_lightningPath is { Count: > 1 })
            {
                var strokeAlpha = (byte)(Math.Clamp(_lightningFlash * intensity * 220, 0, 255));
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(strokeAlpha, 255, 255, 255)), 2);
                for (var i = 0; i < _lightningPath.Count - 1; i++)
                {
                    context.DrawLine(pen, _lightningPath[i], _lightningPath[i + 1]);
                }
            }
        }
    }

    private struct RainDrop(double x, double y, double vy, double len, double alpha)
    {
        public double X = x;
        public double Y = y;
        public double Vy = vy;
        public double Len = len;
        public double Alpha = alpha;
    }

    private struct SnowFlake(double x, double y, double vx, double vy, double r, double alpha)
    {
        public double X = x;
        public double Y = y;
        public double Vx = vx;
        public double Vy = vy;
        public double R = r;
        public double Alpha = alpha;
    }

    private struct SandGrain(double x, double y, double vx, double vy, double len, double alpha)
    {
        public double X = x;
        public double Y = y;
        public double Vx = vx;
        public double Vy = vy;
        public double Len = len;
        public double Alpha = alpha;
    }

    private struct HazePuff(double x, double y, double vx, double vy, double r, double alpha)
    {
        public double X = x;
        public double Y = y;
        public double Vx = vx;
        public double Vy = vy;
        public double R = r;
        public double Alpha = alpha;

        public static HazePuff Create(Random rng, double w, double h, bool upward, bool dark)
        {
            var baseR = 120 + rng.NextDouble() * 260;
            var x = rng.NextDouble() * w;
            var y = rng.NextDouble() * h;

            // Fog drifts horizontally; smoke drifts upward.
            var vx = (upward ? (-25 + rng.NextDouble() * 50) : (-20 + rng.NextDouble() * 40));
            var vy = upward ? (-(15 + rng.NextDouble() * 25)) : (-3 + rng.NextDouble() * 6);

            var a = dark
                ? (0.05 + rng.NextDouble() * 0.10)
                : (0.05 + rng.NextDouble() * 0.12);

            return new HazePuff(x, y, vx, vy, baseR, a);
        }
    }
}
