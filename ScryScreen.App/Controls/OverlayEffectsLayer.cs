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
    private readonly List<HazePuff> _fog = new();
    private readonly List<HazePuff> _smoke = new();

    private DateTime _lastTickUtc = DateTime.UtcNow;

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

    private bool AnyEnabled =>
        RainEnabled || SnowEnabled || FogEnabled || SmokeEnabled || LightningEnabled;

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

        if (!AnyEnabled)
        {
            // Decay flash even if toggled off.
            _lightningFlash = Math.Max(0, _lightningFlash - dt * 6);
            _lightningPath = null;

            if (_rain.Count > 0 || _snow.Count > 0 || _fog.Count > 0 || _smoke.Count > 0)
            {
                _rain.Clear();
                _snow.Clear();
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

        var target = (int)(120 * intensity);
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

        var target = (int)(90 * intensity);
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

    private void UpdateFog(double dt, double w, double h)
    {
        var intensity = Clamp01(FogIntensity);
        if (!FogEnabled || intensity <= 0)
        {
            _fog.Clear();
            return;
        }

        var target = 6 + (int)(10 * intensity);
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

        var target = 5 + (int)(9 * intensity);
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

        // Trigger probability based on intensity (~0.05-0.25 flashes/sec).
        var flashesPerSecond = 0.06 + (0.25 * intensity);
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
            var a = (byte)(Clamp01(FogIntensity) * 50);
            if (a > 0)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(a, 160, 190, 220)), null, new Rect(0, 0, w, h));
            }

            foreach (var p in _fog)
            {
                var brush = new SolidColorBrush(Color.FromArgb((byte)(p.Alpha * 255), 180, 205, 230));
                context.DrawEllipse(brush, null, new Point(p.X, p.Y), p.R, p.R * 0.75);
            }
        }

        if (SmokeEnabled)
        {
            var a = (byte)(Clamp01(SmokeIntensity) * 40);
            if (a > 0)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(a, 30, 35, 45)), null, new Rect(0, 0, w, h));
            }

            foreach (var p in _smoke)
            {
                var brush = new SolidColorBrush(Color.FromArgb((byte)(p.Alpha * 255), 40, 45, 55));
                context.DrawEllipse(brush, null, new Point(p.X, p.Y), p.R, p.R);
            }
        }

        if (RainEnabled)
        {
            foreach (var d in _rain)
            {
                var c = Color.FromArgb((byte)(d.Alpha * 255), 190, 220, 255);
                var pen = new Pen(new SolidColorBrush(c), 1);
                context.DrawLine(pen, new Point(d.X, d.Y), new Point(d.X, d.Y + d.Len));
            }
        }

        if (SnowEnabled)
        {
            foreach (var f in _snow)
            {
                var c = Color.FromArgb((byte)(f.Alpha * 255), 235, 245, 255);
                context.DrawEllipse(new SolidColorBrush(c), null, new Point(f.X, f.Y), f.R, f.R);
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
