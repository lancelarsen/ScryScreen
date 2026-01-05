using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ScryScreen.App.Services;

namespace ScryScreen.App.Controls;

public sealed class OverlayEffectsLayer : Control
{
    // Larger tiles reduce visible repetition and help hide any residual tiling artifacts.
    private const int FogTileSize = 512;
    private const int FogLayerCount = 4;

    private static readonly double[] FogLayerScales = { 4.0, 3.0, 2.3, 1.7 };
    private static readonly double[] FogLayerSpeedX = { -38.0, -26.0, -16.0, -9.0 };
    private static readonly double[] FogLayerSpeedY = { -4.0, -2.5, -1.4, -0.8 };
    private static readonly double[] FogLayerOpacities = { 0.55, 0.38, 0.26, 0.18 };

    private static readonly RadialGradientBrush FogPuffBrush = new()
    {
        Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.60, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.60, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            // Softer falloff to hide hard-ish ellipse edges.
            new GradientStop(Color.FromArgb(205, 205, 230, 250), 0.00),
            new GradientStop(Color.FromArgb(110, 205, 230, 250), 0.28),
            new GradientStop(Color.FromArgb(55, 205, 230, 250), 0.55),
            new GradientStop(Color.FromArgb(0, 205, 230, 250), 1.00),
        }
    };

    private static readonly RadialGradientBrush SmokePuffBrush = new()
    {
        Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.65, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.65, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            // Dark core, soft falloff.
            new GradientStop(Color.FromArgb(210, 35, 38, 45), 0.00),
            new GradientStop(Color.FromArgb(140, 35, 38, 45), 0.22),
            new GradientStop(Color.FromArgb(70, 35, 38, 45), 0.50),
            new GradientStop(Color.FromArgb(0, 35, 38, 45), 1.00),
        }
    };

    private static readonly RadialGradientBrush FirePuffBrush = new()
    {
        Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new GradientStop(Color.FromArgb(255, 255, 235, 155), 0.00),
            new GradientStop(Color.FromArgb(210, 255, 155, 35), 0.25),
            new GradientStop(Color.FromArgb(120, 255, 75, 10), 0.55),
            new GradientStop(Color.FromArgb(0, 255, 75, 10), 1.00),
        }
    };

    private static readonly LinearGradientBrush SandBandMask = new()
    {
        // Repeat a few vertical bands so the sandstorm has that "blowing sheets" look.
        StartPoint = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
        SpreadMethod = GradientSpreadMethod.Repeat,
        GradientStops = new GradientStops
        {
            // More bands = better full-height coverage.
            // Keep soft shoulders so the sheets feel puffy rather than like hard stripes.
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.00),

            // Band 1
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.06),
            new GradientStop(Color.FromArgb(170, 255, 255, 255), 0.10),
            new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.14),
            new GradientStop(Color.FromArgb(120, 255, 255, 255), 0.18),
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.22),

            // Band 2
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.30),
            new GradientStop(Color.FromArgb(155, 255, 255, 255), 0.34),
            new GradientStop(Color.FromArgb(245, 255, 255, 255), 0.39),
            new GradientStop(Color.FromArgb(110, 255, 255, 255), 0.44),
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.48),

            // Band 3
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.56),
            new GradientStop(Color.FromArgb(165, 255, 255, 255), 0.60),
            new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.65),
            new GradientStop(Color.FromArgb(125, 255, 255, 255), 0.70),
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.74),

            // Band 4
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.80),
            new GradientStop(Color.FromArgb(150, 255, 255, 255), 0.84),
            new GradientStop(Color.FromArgb(235, 255, 255, 255), 0.89),
            new GradientStop(Color.FromArgb(100, 255, 255, 255), 0.93),
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.97),

            new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.00),
        }
    };


    public static readonly StyledProperty<bool> RainEnabledProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, bool>(nameof(RainEnabled));

    public static readonly StyledProperty<double> RainIntensityProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, double>(nameof(RainIntensity), defaultValue: 0.5);

    public static readonly StyledProperty<bool> SnowEnabledProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, bool>(nameof(SnowEnabled));

    public static readonly StyledProperty<double> SnowIntensityProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, double>(nameof(SnowIntensity), defaultValue: 0.5);

    public static readonly StyledProperty<bool> AshEnabledProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, bool>(nameof(AshEnabled));

    public static readonly StyledProperty<double> AshIntensityProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, double>(nameof(AshIntensity), defaultValue: 0.5);

    public static readonly StyledProperty<bool> FireEnabledProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, bool>(nameof(FireEnabled));

    public static readonly StyledProperty<double> FireIntensityProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, double>(nameof(FireIntensity), defaultValue: 0.35);

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

    public static readonly StyledProperty<long> LightningTriggerProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, long>(nameof(LightningTrigger), defaultValue: 0);

    public static readonly StyledProperty<bool> EmitAudioEventsProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, bool>(nameof(EmitAudioEvents), defaultValue: false);

    public static readonly StyledProperty<int> AudioPortalNumberProperty =
        AvaloniaProperty.Register<OverlayEffectsLayer, int>(nameof(AudioPortalNumber), defaultValue: 0);

    public bool RainEnabled { get => GetValue(RainEnabledProperty); set => SetValue(RainEnabledProperty, value); }
    public double RainIntensity { get => GetValue(RainIntensityProperty); set => SetValue(RainIntensityProperty, value); }

    public bool SnowEnabled { get => GetValue(SnowEnabledProperty); set => SetValue(SnowEnabledProperty, value); }
    public double SnowIntensity { get => GetValue(SnowIntensityProperty); set => SetValue(SnowIntensityProperty, value); }

    public bool AshEnabled { get => GetValue(AshEnabledProperty); set => SetValue(AshEnabledProperty, value); }
    public double AshIntensity { get => GetValue(AshIntensityProperty); set => SetValue(AshIntensityProperty, value); }

    public bool FireEnabled { get => GetValue(FireEnabledProperty); set => SetValue(FireEnabledProperty, value); }
    public double FireIntensity { get => GetValue(FireIntensityProperty); set => SetValue(FireIntensityProperty, value); }

    public bool SandEnabled { get => GetValue(SandEnabledProperty); set => SetValue(SandEnabledProperty, value); }
    public double SandIntensity { get => GetValue(SandIntensityProperty); set => SetValue(SandIntensityProperty, value); }

    public bool FogEnabled { get => GetValue(FogEnabledProperty); set => SetValue(FogEnabledProperty, value); }
    public double FogIntensity { get => GetValue(FogIntensityProperty); set => SetValue(FogIntensityProperty, value); }

    public bool SmokeEnabled { get => GetValue(SmokeEnabledProperty); set => SetValue(SmokeEnabledProperty, value); }
    public double SmokeIntensity { get => GetValue(SmokeIntensityProperty); set => SetValue(SmokeIntensityProperty, value); }

    public bool LightningEnabled { get => GetValue(LightningEnabledProperty); set => SetValue(LightningEnabledProperty, value); }
    public double LightningIntensity { get => GetValue(LightningIntensityProperty); set => SetValue(LightningIntensityProperty, value); }
    public long LightningTrigger { get => GetValue(LightningTriggerProperty); set => SetValue(LightningTriggerProperty, value); }

    public bool EmitAudioEvents { get => GetValue(EmitAudioEventsProperty); set => SetValue(EmitAudioEventsProperty, value); }
    public int AudioPortalNumber { get => GetValue(AudioPortalNumberProperty); set => SetValue(AudioPortalNumberProperty, value); }

    private readonly DispatcherTimer _timer;
    private readonly Random _rng = new();

    private readonly List<RainDrop> _rain = new();
    private readonly List<SnowFlake> _snow = new();
    private readonly List<SnowFlake> _ash = new();
    private readonly List<FirePuff> _fire = new();
    private readonly List<Ember> _embers = new();
    private readonly List<SandGrain> _sand = new();
    private readonly List<HazePuff> _fog = new();
    private readonly List<HazePuff> _smoke = new();

    private readonly Pen?[] _rainPens = new Pen?[256];
    private readonly SolidColorBrush?[] _snowBrushes = new SolidColorBrush?[256];
    private readonly SolidColorBrush?[] _ashBrushes = new SolidColorBrush?[256];
    private readonly SolidColorBrush?[] _emberHotBrushes = new SolidColorBrush?[256];
    private readonly SolidColorBrush?[] _emberMidBrushes = new SolidColorBrush?[256];
    private readonly SolidColorBrush?[] _emberCoolBrushes = new SolidColorBrush?[256];
    private readonly Pen?[] _sandPens = new Pen?[256];

    private readonly WriteableBitmap?[] _fogLayerTiles = new WriteableBitmap?[FogLayerCount];
    private readonly WriteableBitmap?[] _sandBandTiles = new WriteableBitmap?[FogLayerCount];

    private DateTime _lastTickUtc = DateTime.UtcNow;
    private double _timeSeconds;

    private double _lastBoundsW = double.NaN;
    private double _lastBoundsH = double.NaN;

    private double _lightningFlash;
    private double _lightningTimeToNextStrike;
    private int _lightningBurstRemaining;
    private double _lightningTimeToNextBurstStrike;

    private int _lightningPulsesRemaining;
    private double _lightningPulseTime;
    private double _lightningPulseDuration;
    private double _lightningInterPulseTimer;

    private bool _lightningPrevEnabled;
    private double _lightningPrevDensity;
    private long _lightningPrevTrigger;

    public OverlayEffectsLayer()
    {
        IsHitTestVisible = false;

        // Fog/smoke rely on scaled bitmaps; force good filtering to avoid edge/seam artifacts.
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, (_, _) => Tick());
        this.AttachedToVisualTree += (_, _) => { _lastTickUtc = DateTime.UtcNow; _timer.Start(); };
        this.DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    private static double FireRegionHeight(double h, double density)
    {
        // Density is allowed to exceed 1.0 (overdrive up to ~5). We map 0.1..5 => 0..1.
        // IMPORTANT: Fire should read as bottom-anchored "flashes" rather than filling the
        // entire portal at higher values. Keep the region relatively shallow and make higher
        // intensities feel stronger via motion + particle count, not height.
        var cappedDensity = Math.Min(ClampMin0(density), 5.0);
        var t = Clamp01((cappedDensity - 0.1) / 4.9);

        // 12%..30% of portal height; slight growth at high intensity.
        var frac = 0.12 + (0.18 * Math.Pow(t, 0.90));
        return h * Math.Clamp(frac, 0.12, 0.30);
    }

    private static double ClampMin0(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
        {
            return 0;
        }

        return v < 0 ? 0 : v;
    }

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

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        var t = (x - edge0) / (edge1 - edge0);
        t = t < 0 ? 0 : (t > 1 ? 1 : t);
        return t * t * (3 - (2 * t));
    }

    private static uint Hash(uint x)
    {
        // Simple integer hash for stable pseudo-random gradients.
        x ^= x >> 16;
        x *= 0x7feb352d;
        x ^= x >> 15;
        x *= 0x846ca68b;
        x ^= x >> 16;
        return x;
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float Fade(float t)
    {
        // Smooth interpolation; similar to Perlin fade.
        return t * t * t * (t * ((t * 6) - 15) + 10);
    }

    private static float ValueAt(int x, int y, int seed)
    {
        var h = Hash((uint)(x * 374761393) ^ (uint)(y * 668265263) ^ (uint)(seed * 1442695041));
        return (h & 0x00FFFFFF) / 16777215f; // 0..1
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var x1 = x0 + 1;
        var y1 = y0 + 1;

        var tx = x - x0;
        var ty = y - y0;
        var u = Fade(tx);
        var v = Fade(ty);

        var a = ValueAt(x0, y0, seed);
        var b = ValueAt(x1, y0, seed);
        var c = ValueAt(x0, y1, seed);
        var d = ValueAt(x1, y1, seed);

        var ab = Lerp(a, b, u);
        var cd = Lerp(c, d, u);
        return Lerp(ab, cd, v);
    }

    private static float FractalNoise(float x, float y, int seed, int octaves)
    {
        float sum = 0;
        float amp = 1;
        float freq = 1;
        float norm = 0;

        for (var i = 0; i < octaves; i++)
        {
            sum += amp * ValueNoise(x * freq, y * freq, seed + (i * 1013));
            norm += amp;
            amp *= 0.5f;
            freq *= 2.0f;
        }

        return sum / MathF.Max(0.0001f, norm);
    }

    private static float SeamlessFogNoise(float u, float v, int seed, float structureScale, int octaves)
    {
        // u/v are in [0..1]. Blend 4 samples to make a seamless, tileable noise field.
        // This prevents visible seams at fog tile boundaries when the texture is repeated.
        var fu = Fade(u);
        var fv = Fade(v);

        float Sample(float uu, float vv)
        {
            // Fractal value noise with stronger domain warping + a slight domain rotation.
            // This reduces axis-aligned artifacts that can show up as straight-ish lines.
            var ux = uu * structureScale;
            var uy = vv * structureScale;

            // Rotate domain to avoid grid alignment.
            const float ca = 0.8253356f; // cos(0.6)
            const float sa = 0.5649858f; // sin(0.6)
            var rx = (ux * ca) - (uy * sa);
            var ry = (ux * sa) + (uy * ca);

            // Two independent warps (not the same value) to avoid directional streaks.
            var w1 = FractalNoise((rx * 0.85f) + 17.3f, (ry * 0.85f) - 9.7f, seed + 17, octaves: 3);
            var w2 = FractalNoise((rx * 0.65f) - 41.1f, (ry * 0.65f) + 33.8f, seed + 51, octaves: 3);

            var fx = rx + ((w1 - 0.5f) * 1.35f) + ((w2 - 0.5f) * 0.85f);
            var fy = ry + ((w2 - 0.5f) * 1.20f) - ((w1 - 0.5f) * 0.70f);

            return FractalNoise(fx, fy, seed, octaves);
        }

        var n00 = Sample(u, v);
        var n10 = Sample(u + 1, v);
        var n01 = Sample(u, v + 1);
        var n11 = Sample(u + 1, v + 1);

        var nx0 = Lerp(n00, n10, fu);
        var nx1 = Lerp(n01, n11, fu);
        return Lerp(nx0, nx1, fv);
    }

    private static float TinyDither01(int x, int y, int seed)
    {
        // Very small stable dither in [0..1] to break up banding.
        var h = Hash((uint)(x * 1597334677) ^ (uint)(y * 3812015801) ^ (uint)(seed * 1103515245));
        return (h & 0xFFFF) / 65535f;
    }

    private void EnsureFogTiles()
    {
        if (_fogLayerTiles[0] is not null)
        {
            return;
        }

        // Slightly different tint/structure per layer so they don't stack into one flat sheet.
        _fogLayerTiles[0] = CreateFogTile(seed: 1337, baseColor: new Color(255, 195, 220, 245), structureScale: 2.2f, octaves: 5, alphaBias: 0.38f);
        _fogLayerTiles[1] = CreateFogTile(seed: 7331, baseColor: new Color(255, 205, 230, 250), structureScale: 3.1f, octaves: 5, alphaBias: 0.42f);
        _fogLayerTiles[2] = CreateFogTile(seed: 4242, baseColor: new Color(255, 185, 210, 235), structureScale: 4.2f, octaves: 4, alphaBias: 0.46f);
        _fogLayerTiles[3] = CreateFogTile(seed: 9001, baseColor: new Color(255, 175, 200, 225), structureScale: 5.4f, octaves: 4, alphaBias: 0.50f);
    }

    private void EnsureSandBandTiles()
    {
        if (_sandBandTiles[0] is not null)
        {
            return;
        }

        // Warm / sandy tint, tuned to read like dusty sheets rather than fog.
        _sandBandTiles[0] = CreateFogTile(seed: 2468, baseColor: new Color(255, 235, 210, 150), structureScale: 2.0f, octaves: 4, alphaBias: 0.40f);
        _sandBandTiles[1] = CreateFogTile(seed: 8642, baseColor: new Color(255, 230, 195, 130), structureScale: 3.0f, octaves: 4, alphaBias: 0.45f);
        _sandBandTiles[2] = CreateFogTile(seed: 1357, baseColor: new Color(255, 220, 185, 120), structureScale: 4.0f, octaves: 3, alphaBias: 0.50f);
        _sandBandTiles[3] = CreateFogTile(seed: 9753, baseColor: new Color(255, 210, 175, 110), structureScale: 5.0f, octaves: 3, alphaBias: 0.54f);
    }

    private static WriteableBitmap CreateFogTile(int seed, Color baseColor, float structureScale, int octaves, float alphaBias)
    {
        var bmp = new WriteableBitmap(
            new PixelSize(FogTileSize, FogTileSize),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var fb = bmp.Lock();
        var stride = fb.RowBytes;
        var data = new byte[stride * FogTileSize];

        for (var y = 0; y < FogTileSize; y++)
        {
            // Use [0..1) UVs (not inclusive of 1.0) so the last pixel doesn't duplicate the first.
            // This reduces visible edge artifacts when the tile is repeated with filtering.
            var ny = y / (float)FogTileSize;
            var rowBase = y * stride;
            for (var x = 0; x < FogTileSize; x++)
            {
                var nx = x / (float)FogTileSize;

                // Seamless, tileable fog texture. The vertical density gradient is applied globally at render time
                // (via an opacity mask) so it does not repeat per-tile and create hard horizontal bands.
                var n = SeamlessFogNoise(nx, ny, seed, structureScale, octaves);
                // Push towards wispy shapes.
                n = MathF.Pow(n, 1.7f);

                // Convert noise to alpha. alphaBias shifts how much is visible.
                var a01 = (n - alphaBias) / (1.0f - alphaBias);
                if (a01 < 0) a01 = 0;
                if (a01 > 1) a01 = 1;

                // Tiny dither helps hide 8-bit alpha banding on large smooth gradients.
                a01 = Math.Clamp(a01 + ((TinyDither01(x, y, seed) - 0.5f) * (1f / 255f)), 0f, 1f);

                var a = (byte)(a01 * 255);

                // Premultiply for AlphaFormat.Premul.
                var pr = (byte)((baseColor.R * a) / 255);
                var pg = (byte)((baseColor.G * a) / 255);
                var pb = (byte)((baseColor.B * a) / 255);

                var idx = rowBase + (x * 4);
                data[idx + 0] = pb;
                data[idx + 1] = pg;
                data[idx + 2] = pr;
                data[idx + 3] = a;
            }
        }

        Marshal.Copy(data, 0, fb.Address, data.Length);

        return bmp;
    }


    private bool AnyEnabled =>
        RainEnabled || SnowEnabled || AshEnabled || FireEnabled || SandEnabled || FogEnabled || SmokeEnabled || LightningEnabled;

    private void RescaleParticlesForNewBounds(double w, double h)
    {
        if (double.IsNaN(_lastBoundsW) || double.IsNaN(_lastBoundsH) || _lastBoundsW <= 0 || _lastBoundsH <= 0)
        {
            _lastBoundsW = w;
            _lastBoundsH = h;
            return;
        }

        if (Math.Abs(_lastBoundsW - w) < 0.5 && Math.Abs(_lastBoundsH - h) < 0.5)
        {
            return;
        }

        var sx = w / _lastBoundsW;
        var sy = h / _lastBoundsH;

        if (double.IsNaN(sx) || double.IsInfinity(sx) || sx <= 0 || double.IsNaN(sy) || double.IsInfinity(sy) || sy <= 0)
        {
            _lastBoundsW = w;
            _lastBoundsH = h;
            return;
        }

        for (var i = 0; i < _rain.Count; i++)
        {
            var d = _rain[i];
            d.X *= sx;
            d.Y *= sy;
            d.Vy *= sy;
            d.Len *= sy;
            _rain[i] = d;
        }

        void RescaleFlakes(List<SnowFlake> flakes)
        {
            for (var i = 0; i < flakes.Count; i++)
            {
                var f = flakes[i];
                f.X *= sx;
                f.Y *= sy;
                f.Vx *= sx;
                f.Vy *= sy;
                f.R *= Math.Sqrt(sx * sy);
                flakes[i] = f;
            }
        }

        RescaleFlakes(_snow);
        RescaleFlakes(_ash);

        for (var i = 0; i < _fire.Count; i++)
        {
            var p = _fire[i];
            p.X *= sx;
            p.Y *= sy;
            p.Vx *= sx;
            p.Vy *= sy;
            p.R *= Math.Sqrt(sx * sy);
            _fire[i] = p;
        }

        for (var i = 0; i < _embers.Count; i++)
        {
            var e = _embers[i];
            e.X *= sx;
            e.Y *= sy;
            e.Vx *= sx;
            e.Vy *= sy;
            e.R *= Math.Sqrt(sx * sy);
            _embers[i] = e;
        }

        for (var i = 0; i < _sand.Count; i++)
        {
            var g = _sand[i];
            g.X *= sx;
            g.Y *= sy;
            g.Vx *= sx;
            g.Vy *= sy;
            g.Len *= Math.Sqrt(sx * sy);
            _sand[i] = g;
        }

        void RescalePuffs(List<HazePuff> puffs)
        {
            for (var i = 0; i < puffs.Count; i++)
            {
                var p = puffs[i];
                p.X *= sx;
                p.Y *= sy;
                p.Vx *= sx;
                p.Vy *= sy;
                p.R *= Math.Sqrt(sx * sy);
                puffs[i] = p;
            }
        }

        RescalePuffs(_fog);
        RescalePuffs(_smoke);

        _lastBoundsW = w;
        _lastBoundsH = h;
    }

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
            _lightningFlash = Math.Max(0, _lightningFlash - dt * 10);
            _lightningTimeToNextStrike = 0;
            _lightningBurstRemaining = 0;
            _lightningTimeToNextBurstStrike = 0;
            _lightningPulsesRemaining = 0;
            _lightningPulseTime = 0;
            _lightningPulseDuration = 0;
            _lightningInterPulseTimer = 0;

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

        // When the portal/preview size changes (DPI, fullscreen transition, viewbox scaling, etc.),
        // keep existing particles anchored to the same relative location. Otherwise effects can
        // appear shifted while masks/brightness update immediately.
        RescaleParticlesForNewBounds(w, h);

        UpdateRain(dt, w, h);
        UpdateSnow(dt, w, h);
        UpdateAsh(dt, w, h);
        UpdateFire(dt, w, h);
        UpdateSand(dt, w, h);
        UpdateFog(dt, w, h);
        UpdateSmoke(dt, w, h);
        UpdateLightning(dt, w, h);

        InvalidateVisual();
    }

    private void UpdateRain(double dt, double w, double h)
    {
        var density = ClampMin0(RainIntensity);
        var level = Clamp01(density);
        if (!RainEnabled || density <= 0)
        {
            _rain.Clear();
            return;
        }

        // 10x baseline * 3x requested boost.
        // For 0..1, keep the same curve behavior; above 1, scale density linearly.
        var scale = density <= 1 ? 1 : Math.Min(density, 50);
        var target = (int)(3600 * DensityCurveCubic(level) * scale);
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
        var density = ClampMin0(SnowIntensity);
        var level = Clamp01(density);
        if (!SnowEnabled || density <= 0)
        {
            _snow.Clear();
            return;
        }

        // 10x baseline * 3x requested boost.
        // For 0..1, keep the same curve behavior; above 1, scale density linearly.
        var scale = density <= 1 ? 1 : Math.Min(density, 50);
        var target = (int)(2700 * DensityCurveCubic(level) * scale);
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

    private void UpdateAsh(double dt, double w, double h)
    {
        var density = ClampMin0(AshIntensity);
        var level = Clamp01(density);
        if (!AshEnabled || density <= 0)
        {
            _ash.Clear();
            return;
        }

        // Similar to snow, but slightly slower and smaller flakes.
        var scale = density <= 1 ? 1 : Math.Min(density, 50);
        var target = (int)(2200 * DensityCurveCubic(level) * scale);
        while (_ash.Count < target)
        {
            _ash.Add(new SnowFlake(
                x: _rng.NextDouble() * w,
                y: _rng.NextDouble() * h,
                vx: -18 + _rng.NextDouble() * 36,
                vy: 25 + _rng.NextDouble() * 65,
                r: 0.9 + _rng.NextDouble() * 1.8,
                alpha: 0.10 + _rng.NextDouble() * 0.18));
        }

        for (var i = _ash.Count - 1; i >= 0; i--)
        {
            var f = _ash[i];
            f.X += f.Vx * dt;
            f.Y += f.Vy * dt;

            // Gentle wobble.
            f.X += Math.Sin((f.Y / 28.0) + i) * dt * 10;

            if (f.Y - f.R > h)
            {
                f.Y = -_rng.NextDouble() * 80;
                f.X = _rng.NextDouble() * w;
            }

            if (f.X < -20) f.X = w + 20;
            if (f.X > w + 20) f.X = -20;

            _ash[i] = f;
        }

        if (_ash.Count > target)
        {
            _ash.RemoveRange(target, _ash.Count - target);
        }
    }

    private void UpdateFire(double dt, double w, double h)
    {
        var density = ClampMin0(FireIntensity);
        if (!FireEnabled || density <= 0)
        {
            _fire.Clear();
            _embers.Clear();
            return;
        }

        var cappedDensity = Math.Min(density, 5.0);
        var t = Clamp01((cappedDensity - 0.1) / 4.9);

        // Keep the base pinned to the bottom.
        var fireHeight = FireRegionHeight(h, density);
        var topLimit = h - fireHeight;

        // Always keep a dense "base" band of flames near the bottom; as intensity rises,
        // add more puffs that reach higher (within the fire region) rather than lifting the base.
        var baseTopLimit = h - (fireHeight * 0.60);

        // Keep the region shallow; drive perceived intensity through volume + motion.
        // Use a non-linear curve so high intensities really pack in detail.
        var puffCurve = Math.Pow(t, 1.35);
        var emberCurve = Math.Pow(t, 1.25);
        var targetPuffs = (int)(28 + (720 * puffCurve));
        var targetEmbers = (int)(120 + (1100 * emberCurve));

        // Upper-layer puffs increase as the fire height grows; base puffs remain dense.
        var upperFrac = Math.Clamp(0.20 + (0.55 * t), 0.20, 0.80);
        var targetUpperPuffs = (int)Math.Round(targetPuffs * upperFrac);
        var targetBasePuffs = Math.Max(0, targetPuffs - targetUpperPuffs);

        var baseCount = 0;
        var upperCount = 0;
        for (var i = 0; i < _fire.Count; i++)
        {
            if (_fire[i].IsBaseLayer) baseCount++;
            else upperCount++;
        }

        while (baseCount < targetBasePuffs)
        {
            _fire.Add(FirePuff.Create(_rng, w, h, baseLevel: t, baseLayer: true));
            baseCount++;
        }

        while (upperCount < targetUpperPuffs)
        {
            _fire.Add(FirePuff.Create(_rng, w, h, baseLevel: t, baseLayer: false));
            upperCount++;
        }

        while (_embers.Count < targetEmbers)
        {
            _embers.Add(Ember.Create(_rng, w, h, baseLevel: t));
        }

        // Update flame puffs.
        for (var i = _fire.Count - 1; i >= 0; i--)
        {
            var p = _fire[i];
            p.Age += dt;

            // More vigorous flicker as intensity rises.
            var flickerFreq = 8.5 + (6.5 * t);
            var flicker = 0.65 + (0.35 * Math.Sin((_timeSeconds * flickerFreq) + p.Phase));
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            p.X += Math.Sin((p.Y / 30.0) + p.Phase) * dt * (22 + (42 * t));
            p.R *= 0.9990; // slow shrink

            var layerTopLimit = p.IsBaseLayer ? baseTopLimit : topLimit;

            // Respawn if it goes above its layer / too small.
            if (p.Y + p.R < layerTopLimit - 20 || p.R < 6)
            {
                _fire[i] = FirePuff.Create(_rng, w, h, baseLevel: t, baseLayer: p.IsBaseLayer);
                continue;
            }

            // Keep roughly inside width.
            if (p.X < -p.R) p.X = w + p.R;
            if (p.X > w + p.R) p.X = -p.R;

            p.Flicker = flicker;
            _fire[i] = p;
        }

        // Update embers. Embers should rise ~2x the fire height.
        var emberHeight = Math.Min(h, fireHeight * 2.0);
        var emberTopLimit = h - emberHeight;

        for (var i = _embers.Count - 1; i >= 0; i--)
        {
            var e = _embers[i];
            e.Age += dt;
            e.X += e.Vx * dt;
            e.Y += e.Vy * dt;
            e.X += Math.Sin((_timeSeconds * 3.5) + e.Phase) * dt * 12;

            // Cool + fade as it rises.
            e.Heat = Math.Max(0, e.Heat - (dt * (0.18 + (0.22 * t))));

            // Respawn if it rises above the ember region / cooled out.
            if (e.Y + 40 < emberTopLimit - 20 || e.Y < -120 || e.Heat <= 0.02)
            {
                _embers[i] = Ember.Create(_rng, w, h, baseLevel: t);
                continue;
            }

            if (e.X < -20) e.X = w + 20;
            if (e.X > w + 20) e.X = -20;

            _embers[i] = e;
        }

        // Trim any extras (in case of large target swings).
        if (_fire.Count > targetPuffs)
        {
            _fire.RemoveRange(targetPuffs, _fire.Count - targetPuffs);
        }

        if (_embers.Count > targetEmbers)
        {
            _embers.RemoveRange(targetEmbers, _embers.Count - targetEmbers);
        }
    }

    private void UpdateSand(double dt, double w, double h)
    {
        var density = ClampMin0(SandIntensity);
        var level = Clamp01(density);
        if (!SandEnabled || density <= 0)
        {
            _sand.Clear();
            return;
        }

        // A sand storm is a lot of small, fast streaks.
        // Keep it dense but still performant.
        // 10x baseline * 3x requested boost.
        var scale = density <= 1 ? 1 : Math.Min(density, 50);
        var target = (int)(4200 * (0.20 + (2.20 * level * level)) * scale);
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
            g.Y += Math.Sin((g.X / 60.0) + i) * dt * (40 * level);

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
        var density = ClampMin0(FogIntensity);
        var level = Clamp01(density);
        if (!FogEnabled || density <= 0)
        {
            _fog.Clear();
            return;
        }

        // 3x volume requested.
        // Above 1, add more flowing layers (cap to avoid runaway allocations).
        var scale = density <= 1 ? 1 : Math.Min(density, 8);
        var target = (int)((18 + (30 * level)) * scale);
        while (_fog.Count < target)
        {
            _fog.Add(HazePuff.Create(_rng, w, h, upward: false, dark: false));
        }

        // Movement scales with intensity; above 1.0, increase speed/flow noticeably.
        var speedScale = 0.55 + (1.10 * level);
        if (density > 1)
        {
            speedScale *= 1.0 + (Math.Min(density, 4) - 1.0) * 0.65;
        }

        for (var i = _fog.Count - 1; i >= 0; i--)
        {
            var p = _fog[i];

            // Add a bit of swirly drift so it doesn't read as straight translation.
            var t = _timeSeconds;
            var swirl = Math.Sin((t * 0.45) + (i * 0.37)) * (10 + (40 * level));
            var swirl2 = Math.Cos((t * 0.32) + (i * 0.51)) * (8 + (30 * level));

            p.X += ((p.Vx * speedScale) + swirl) * dt;
            p.Y += ((p.Vy * speedScale) + (swirl2 * 0.35)) * dt;

            // Wrap with generous margins so there are always puffs partly off-screen.
            var mx = p.R + 260;
            var my = p.R + 280;
            if (p.X < -mx) p.X = w + mx;
            if (p.X > w + mx) p.X = -mx;
            if (p.Y < -my) p.Y = h + my;
            if (p.Y > h + my) p.Y = -my;

            _fog[i] = p;
        }

        if (_fog.Count > target)
        {
            _fog.RemoveRange(target, _fog.Count - target);
        }
    }

    private void UpdateSmoke(double dt, double w, double h)
    {
        var density = ClampMin0(SmokeIntensity);
        var level = Clamp01(density);
        if (!SmokeEnabled || density <= 0)
        {
            _smoke.Clear();
            return;
        }

        // Above 1, add more puffs (cap to avoid runaway allocations).
        var scale = density <= 1 ? 1 : Math.Min(density, 6);
        var target = (int)((12 + (20 * level)) * scale);
        while (_smoke.Count < target)
        {
            var p = HazePuff.Create(_rng, w, h, upward: true, dark: true);
            var pad = p.R * 0.70;
            p.X = (-pad) + (_rng.NextDouble() * (w + (pad * 2)));
            p.Y = h + (_rng.NextDouble() * (h * 0.25)) + pad;
            _smoke.Add(p);
        }

        var speedScale = 0.70 + (1.20 * level);
        if (density > 1)
        {
            speedScale *= 1.0 + (Math.Min(density, 4) - 1.0) * 0.75;
        }

        for (var i = _smoke.Count - 1; i >= 0; i--)
        {
            var p = _smoke[i];

            var t = _timeSeconds;
            var swirl = Math.Sin((t * 0.55) + (i * 0.41)) * (10 + (55 * level));
            var swirl2 = Math.Cos((t * 0.38) + (i * 0.57)) * (9 + (45 * level));

            // Upward drift + sideways billow.
            p.X += ((p.Vx * 0.70 * speedScale) + (swirl * 0.75)) * dt;
            p.Y += ((p.Vy * 1.15 * speedScale) - (Math.Abs(swirl2) * 0.55)) * dt;

            // Billow/expand as it rises and gradually thin.
            p.R += dt * ((10 + (_rng.NextDouble() * 18)) * (0.65 + (0.85 * level)));
            p.Alpha = Math.Max(0, p.Alpha - dt * (0.008 + ((1 - level) * 0.018)));

            if (p.Alpha <= 0 || p.Y + p.R < -240)
            {
                var np = HazePuff.Create(_rng, w, h, upward: true, dark: true);
                var pad = np.R * 0.70;
                np.X = (-pad) + (_rng.NextDouble() * (w + (pad * 2)));
                np.Y = h + (_rng.NextDouble() * (h * 0.25)) + pad;
                _smoke[i] = np;
                continue;
            }

            var mx = p.R + 220;
            if (p.X < -mx) p.X = w + mx;
            if (p.X > w + mx) p.X = -mx;

            _smoke[i] = p;
        }

        if (_smoke.Count > target)
        {
            _smoke.RemoveRange(target, _smoke.Count - target);
        }
    }

    private void UpdateLightning(double dt, double w, double h)
    {
        var density = ClampMin0(LightningIntensity);
        if (!LightningEnabled || density <= 0)
        {
            _lightningFlash = Math.Max(0, _lightningFlash - dt * 10);
            _lightningTimeToNextStrike = 0;
            _lightningBurstRemaining = 0;
            _lightningTimeToNextBurstStrike = 0;
            _lightningPulsesRemaining = 0;
            _lightningPulseTime = 0;
            _lightningPulseDuration = 0;
            _lightningInterPulseTimer = 0;

            _lightningPrevEnabled = false;
            _lightningPrevDensity = 0;
            _lightningPrevTrigger = 0;
            return;
        }

        // Target behavior:
        // - At 0.1: ~1 strike every 45–60s
        // - As intensity rises (and can go >1): frequency increases and bursts become more common
        var cappedDensity = Math.Min(density, 5.0);
        var t = Clamp01((cappedDensity - 0.1) / 4.9); // 0.1..5 => 0..1
        var ease = t * t;

        double NextIntervalSeconds()
        {
            var min = 45.0 - (42.0 * ease); // 45 -> 3
            var max = 60.0 - (53.0 * ease); // 60 -> 7
            if (max < min)
            {
                (min, max) = (max, min);
            }

            return min + (_rng.NextDouble() * (max - min));
        }

        int NextBurstCount()
        {
            // 1 is most common, 2 less common, 3 rare.
            // As intensity increases, 2/3 become more likely.
            var p3 = 0.02 + (0.16 * ease);
            var p2 = 0.08 + (0.30 * ease);
            var r = _rng.NextDouble();
            if (r < p3)
            {
                return 3;
            }

            if (r < p3 + p2)
            {
                return 2;
            }

            return 1;
        }

        double NextBurstGapSeconds()
        {
            // Time between strikes in a burst.
            var baseGap = 0.55 - (0.37 * ease); // 0.55 -> 0.18
            return baseGap + (_rng.NextDouble() * 0.12);
        }

        void StartStrikePulses()
        {
            // Flash-only: simulate lightning with a quick flicker sequence.
            var basePulses = 2 + (int)Math.Floor(ease * 3.0); // 2..5
            var pulses = basePulses;
            if (_rng.NextDouble() < 0.35)
            {
                pulses++;
            }

            _lightningPulsesRemaining = Math.Clamp(pulses, 2, 6);
            _lightningPulseTime = 0;
            _lightningInterPulseTimer = 0;
            _lightningPulseDuration = (0.06 + (_rng.NextDouble() * 0.05)) + ((1.0 - ease) * 0.03);
            _lightningFlash = 1.0;

            if (EmitAudioEvents)
            {
                EffectsEventBus.RaiseLightningFlash(AudioPortalNumber, cappedDensity);
            }
        }

        // Manual trigger from UI (a nonce that increments).
        if (LightningTrigger != _lightningPrevTrigger)
        {
            _lightningPrevTrigger = LightningTrigger;
            _lightningBurstRemaining = 0;
            _lightningTimeToNextBurstStrike = 0;
            _lightningTimeToNextStrike = 0;
            StartStrikePulses();
        }

        // Prime the system so users can tell it's working:
        // - on first enable, or when cranking intensity up, schedule the next strike soon (especially at high values)
        // - after that, the steady-state interval rules apply
        if (!_lightningPrevEnabled)
        {
            _lightningTimeToNextStrike = cappedDensity >= 1.0
                ? (0.5 + (_rng.NextDouble() * 1.5))
                : 0; // keep strict low-end behavior (45–60s) once it schedules below
        }
        else if (cappedDensity >= 1.0 && (cappedDensity - _lightningPrevDensity) >= 0.5)
        {
            _lightningTimeToNextStrike = Math.Min(_lightningTimeToNextStrike, 0.5 + (_rng.NextDouble() * 1.5));
        }

        _lightningPrevEnabled = true;
        _lightningPrevDensity = cappedDensity;

        // Update pulse state.
        if (_lightningPulsesRemaining > 0)
        {
            if (_lightningInterPulseTimer > 0)
            {
                _lightningInterPulseTimer = Math.Max(0, _lightningInterPulseTimer - dt);
                _lightningFlash = Math.Max(0, _lightningFlash - dt * 12);
            }
            else
            {
                _lightningPulseTime += dt;
                var k = _lightningPulseDuration <= 0 ? 1.0 : (_lightningPulseTime / _lightningPulseDuration);
                _lightningFlash = Math.Max(0, 1.0 - k);

                if (k >= 1.0)
                {
                    _lightningPulsesRemaining--;
                    _lightningPulseTime = 0;
                    _lightningFlash = 0;

                    if (_lightningPulsesRemaining > 0)
                    {
                        _lightningInterPulseTimer = 0.03 + (_rng.NextDouble() * 0.08);
                        _lightningPulseDuration = (0.05 + (_rng.NextDouble() * 0.045)) + ((1.0 - ease) * 0.02);
                    }
                }
            }
        }
        else
        {
            _lightningFlash = Math.Max(0, _lightningFlash - dt * 10);
        }

        // Don't start a new strike while we're in the middle of flicker pulses.
        if (_lightningPulsesRemaining > 0)
        {
            return;
        }

        if (_lightningBurstRemaining > 0)
        {
            _lightningTimeToNextBurstStrike -= dt;
            if (_lightningTimeToNextBurstStrike <= 0)
            {
                StartStrikePulses();
                _lightningBurstRemaining--;

                if (_lightningBurstRemaining > 0)
                {
                    _lightningTimeToNextBurstStrike = NextBurstGapSeconds();
                }
                else
                {
                    _lightningTimeToNextStrike = NextIntervalSeconds();
                }
            }

            return;
        }

        if (_lightningTimeToNextStrike <= 0)
        {
            _lightningTimeToNextStrike = NextIntervalSeconds();
        }

        _lightningTimeToNextStrike -= dt;
        if (_lightningTimeToNextStrike <= 0)
        {
            _lightningBurstRemaining = NextBurstCount();
            _lightningTimeToNextBurstStrike = 0;
        }
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
            var density = ClampMin0(FogIntensity);
            var intensity = Clamp01(density);
            if (intensity > 0)
            {
                // Apply a single global vertical mask so fog naturally pools near the bottom
                // without repeating hard bands at each fog tile boundary.
                var fogMask = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.00),
                        // Bring fog higher up the portal while still pooling toward the bottom.
                        new GradientStop(Color.FromArgb(70, 255, 255, 255), 0.12),
                        new GradientStop(Color.FromArgb(190, 255, 255, 255), 0.55),
                        new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.92),
                        new GradientStop(Color.FromArgb(255, 255, 255, 255), 1.00),
                    }
                };

                using var _mask = context.PushOpacityMask(fogMask, new Rect(0, 0, w, h));

                // A very subtle base wash, then the scrolling layered fog.
                var a = (byte)(intensity * 40);
                if (a > 0)
                {
                    context.DrawRectangle(new SolidColorBrush(Color.FromArgb(a, 160, 190, 220)), null, new Rect(0, 0, w, h));
                }

                // Puff-based fog: lots of soft radial gradients drifting around.
                // This intentionally avoids bitmap tiling so there are no straight seam lines.
                // We still vary opacity with a few pseudo-layers to keep depth.
                var scaleBoost = density <= 1 ? 1 : Math.Min(density, 4);
                var globalOpacity = (0.40 + (0.60 * intensity)) * scaleBoost;

                // Draw from larger/softer to smaller/sharper so it feels layered.
                for (var layer = 0; layer < FogLayerCount; layer++)
                {
                    var layerOpacity = globalOpacity * FogLayerOpacities[layer];
                    if (layerOpacity <= 0)
                    {
                        continue;
                    }

                    var layerRadiusScale = 1.10 - (layer * 0.12);
                    var layerStretchY = 0.85 - (layer * 0.06);
                    var motionBoost = 1.0 + (intensity * 0.9);
                    if (density > 1)
                    {
                        motionBoost *= 1.0 + (Math.Min(density, 4) - 1.0) * 0.85;
                    }

                    var layerOffsetX = Math.Sin((_timeSeconds * 0.22) + (layer * 1.7)) * (w * 0.02 * motionBoost);
                    var layerOffsetY = Math.Sin((_timeSeconds * 0.18) + (layer * 2.3)) * (h * 0.01 * motionBoost);

                    using var _ = context.PushOpacity(layerOpacity);

                    for (var i = 0; i < _fog.Count; i++)
                    {
                        var p = _fog[i];

                        // Each puff has its own alpha; keep it subtle.
                        var puffOpacity = Math.Clamp(p.Alpha * 2.0, 0, 1);
                        if (puffOpacity <= 0)
                        {
                            continue;
                        }

                        using var __ = context.PushOpacity(puffOpacity);

                        var cx = p.X + layerOffsetX;
                        var cy = p.Y + layerOffsetY;

                        // Per-puff shape variation to avoid obvious ellipse edges.
                        var h1 = Hash((uint)(i * 2654435761) ^ 0xA3C59AC3u);
                        var h2 = Hash((uint)(i * 1597334677) ^ 0xC2B2AE35u);
                        var sx = 0.85 + (((h1 & 0xFFFF) / 65535.0) * 0.55);
                        var sy = 0.75 + (((h2 & 0xFFFF) / 65535.0) * 0.65);

                        var rx = p.R * layerRadiusScale * sx;
                        var ry = p.R * layerRadiusScale * layerStretchY * sy;

                        // Draw twice with a slight offset to break symmetry and soften boundaries.
                        context.DrawEllipse(FogPuffBrush, null, new Point(cx, cy), rx, ry);
                        context.DrawEllipse(FogPuffBrush, null, new Point(cx + (rx * 0.12), cy - (ry * 0.08)), rx * 0.75, ry * 0.70);
                    }
                }
            }
        }

        if (SmokeEnabled)
        {
            var density = ClampMin0(SmokeIntensity);
            var intensity = Clamp01(density);
            if (intensity <= 0)
            {
                return;
            }

            // Darken slightly, then draw billowing rising puffs (fog-like but darker).
            var baseA = (byte)(intensity * 28);
            if (baseA > 0)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(baseA, 18, 20, 24)), null, new Rect(0, 0, w, h));
            }

            // Smoke tends to originate lower and thin as it rises.
            var smokeMask = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Color.FromArgb(10, 255, 255, 255), 0.00),
                    new GradientStop(Color.FromArgb(120, 255, 255, 255), 0.40),
                    new GradientStop(Color.FromArgb(255, 255, 255, 255), 1.00),
                }
            };

            using var _mask = context.PushOpacityMask(smokeMask, new Rect(0, 0, w, h));

            var scaleBoost = density <= 1 ? 1 : Math.Min(density, 4);
            var globalOpacity = (0.35 + (0.65 * intensity)) * scaleBoost;
            using var _op = context.PushOpacity(globalOpacity);

            // A couple of pseudo-layers add depth.
            for (var layer = 0; layer < 3; layer++)
            {
                var layerOpacity = (0.55 - (layer * 0.16));
                if (layerOpacity <= 0)
                {
                    continue;
                }

                var layerRadiusScale = 1.25 - (layer * 0.12);
                var layerStretchY = 1.05 + (layer * 0.06);
                var driftX = Math.Sin((_timeSeconds * 0.18) + (layer * 1.9)) * (w * 0.01);

                using var _lop = context.PushOpacity(layerOpacity);

                for (var i = 0; i < _smoke.Count; i++)
                {
                    var p = _smoke[i];
                    var puffOpacity = Math.Clamp(p.Alpha * 2.2, 0, 1);
                    if (puffOpacity <= 0)
                    {
                        continue;
                    }

                    using var __ = context.PushOpacity(puffOpacity);

                    var cx = p.X + driftX;
                    var cy = p.Y;

                    var h1 = Hash((uint)(i * 2246822519) ^ 0x9E3779B9u);
                    var h2 = Hash((uint)(i * 3266489917) ^ 0x85EBCA6Bu);
                    var sx = 0.85 + (((h1 & 0xFFFF) / 65535.0) * 0.60);
                    var sy = 0.90 + (((h2 & 0xFFFF) / 65535.0) * 0.70);

                    var rx = p.R * layerRadiusScale * sx;
                    var ry = p.R * layerRadiusScale * layerStretchY * sy;

                    context.DrawEllipse(SmokePuffBrush, null, new Point(cx, cy), rx, ry);
                    context.DrawEllipse(SmokePuffBrush, null, new Point(cx - (rx * 0.10), cy + (ry * 0.08)), rx * 0.72, ry * 0.66);
                }
            }
        }

        if (SandEnabled)
        {
            var density = ClampMin0(SandIntensity);
            var intensity = Clamp01(density);

            // A subtle warm wash + banded sheets + streaks.
            var a = (byte)(intensity * 22);
            if (a > 0)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(a, 120, 95, 55)), null, new Rect(0, 0, w, h));
            }

            // Add the old "blowing band" look as a sand-colored overlay.
            if (intensity > 0)
            {
                EnsureSandBandTiles();
                RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);

                // Thicken the "puffy sheets" as you push towards the max.
                // Intensity clamps at 1.0, so we use the raw density (slider value) for overdrive above 1.
                var cappedDensity = Math.Min(density, 5.0);
                var overdriveDelta = Math.Max(0.0, cappedDensity - 1.0); // 0..4

                // Stronger ramp toward max so higher values look noticeably thicker.
                // Keep it smooth and bounded with an opacity clamp.
                var overdrive = 1.0
                               + (0.30 * overdriveDelta)
                               + (0.10 * overdriveDelta * overdriveDelta)
                               + (0.02 * overdriveDelta * overdriveDelta * overdriveDelta);

                var bandOpacity = Math.Clamp((0.10 + (0.45 * intensity)) * overdrive, 0.0, 0.98);

                using (context.PushOpacityMask(SandBandMask, new Rect(0, 0, w, h)))
                using (context.PushOpacity(bandOpacity))
                {
                    for (var layer = 0; layer < FogLayerCount; layer++)
                    {
                        var bmp = _sandBandTiles[layer];
                        if (bmp is null)
                        {
                            continue;
                        }

                        // Big scale so it reads as sheets, not as tiny noise.
                        var s = 1.55 + (layer * 0.35);
                        var dstW = FogTileSize * s;
                        var dstH = FogTileSize * s;

                        var speed = (50 + (layer * 28)) * (0.45 + (1.10 * intensity));
                        if (density > 1)
                        {
                            speed *= 1.0 + (Math.Min(density, 5) - 1.0) * 0.65;
                        }

                        var ox = -((_timeSeconds * speed) % dstW);
                        var oy = (Math.Sin((_timeSeconds * 0.35) + layer) * (h * 0.03));

                        // Slight tilt so bands are not perfectly horizontal.
                        var tilt = (-0.06 + (layer * 0.02));
                        using var _tx = context.PushTransform(Matrix.CreateRotation(tilt) * Matrix.CreateTranslation(ox, oy));

                        // Tile across the portal with a small overlap.
                        var xCount = (int)Math.Ceiling((w + dstW) / dstW) + 2;
                        var yCount = (int)Math.Ceiling((h + dstH) / dstH) + 2;
                        for (var yy = -1; yy < yCount; yy++)
                        {
                            for (var xx = -1; xx < xCount; xx++)
                            {
                                var dx = xx * dstW;
                                var dy = yy * dstH;
                                context.DrawImage(bmp, new Rect(0, 0, FogTileSize, FogTileSize), new Rect(dx, dy, dstW, dstH));
                            }
                        }
                    }
                }
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

        if (AshEnabled)
        {
            foreach (var f in _ash)
            {
                var alpha = (byte)Math.Clamp(f.Alpha * 255, 0, 255);
                if (alpha == 0)
                {
                    continue;
                }

                // Black/charcoal flakes.
                var brush = GetCachedBrush(_ashBrushes, alpha, 20, 20, 22);
                context.DrawEllipse(brush, null, new Point(f.X, f.Y), f.R, f.R);
            }
        }

        if (FireEnabled)
        {
            var density = ClampMin0(FireIntensity);
            var cappedDensity = Math.Min(density, 5.0);
            var t = Clamp01((cappedDensity - 0.1) / 4.9);
            // Must match UpdateFire() so flames don't get faded out due to a mismatched topLimit.
            var fireHeight = FireRegionHeight(h, density);
            var topLimit = h - fireHeight;
            var emberHeight = Math.Min(h, fireHeight * 2.0);
            var emberTopLimit = h - emberHeight;

            // Subtle bottom-anchored glow so the fire reads as "pinned" even at low intensity.
            // This also ramps intensity in a way that feels more responsive to the slider.
            var glowHeight = Math.Max(70.0, fireHeight * 0.85);
            var glowStrength = Math.Clamp(0.10 + (0.55 * Math.Pow(t, 0.75)), 0.0, 0.75);
            if (glowStrength > 0.001)
            {
                var a0 = (byte)Math.Clamp(glowStrength * 210, 0, 210);
                var a1 = (byte)Math.Clamp(glowStrength * 140, 0, 140);
                var glow = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop(Color.FromArgb(a0, 255, 120, 20), 0.00),
                        new GradientStop(Color.FromArgb(a1, 255, 170, 60), 0.35),
                        new GradientStop(Color.FromArgb(0, 255, 170, 60), 1.00),
                    }
                };

                context.DrawRectangle(glow, null, new Rect(0, h - glowHeight, w, glowHeight));
            }

            // Flames (soft puffs)
            foreach (var p in _fire)
            {
                // Fade out above the fire region.
                var fade = 1.0 - SmoothStep(topLimit - 55, topLimit + 140, p.Y);
                if (fade <= 0.001)
                {
                    continue;
                }

                var intensityBoost = 0.85 + (0.90 * t);
                var opacity = Math.Clamp(p.Alpha * p.Flicker * fade * intensityBoost, 0, 1);
                if (opacity <= 0.001)
                {
                    continue;
                }

                using (context.PushOpacity(opacity))
                {
                    // Slightly taller ellipses read more like flames.
                    context.DrawEllipse(FirePuffBrush, null, new Point(p.X, p.Y), p.R * 0.85, p.R * 1.25);
                }
            }

            // Embers
            foreach (var e in _embers)
            {
                var fade = 1.0 - SmoothStep(emberTopLimit - 55, emberTopLimit + 220, e.Y);
                if (fade <= 0.001)
                {
                    continue;
                }

                var emberBoost = 0.80 + (1.00 * t);
                var a = (byte)Math.Clamp((e.Alpha * fade * emberBoost) * 255, 0, 255);
                if (a == 0)
                {
                    continue;
                }

                SolidColorBrush brush;
                if (e.Heat > 0.66)
                {
                    brush = GetCachedBrush(_emberHotBrushes, a, 255, 210, 120);
                }
                else if (e.Heat > 0.33)
                {
                    brush = GetCachedBrush(_emberMidBrushes, a, 255, 160, 60);
                }
                else
                {
                    brush = GetCachedBrush(_emberCoolBrushes, a, 220, 90, 30);
                }

                context.DrawEllipse(brush, null, new Point(e.X, e.Y), e.R, e.R);
            }
        }

        if (_lightningFlash > 0 && LightningEnabled)
        {
            var density = ClampMin0(LightningIntensity);
            var cappedDensity = Math.Min(density, 5.0);
            var t = Clamp01((cappedDensity - 0.1) / 4.9);
            var strength = 0.35 + (0.65 * t);
            var overdrive = 1.0 + (0.10 * Math.Max(0.0, cappedDensity - 1.0));

            var flashAlpha = (byte)Math.Clamp(_lightningFlash * strength * overdrive * 235, 0, 235);
            if (flashAlpha > 0)
            {
                var top = flashAlpha;
                var bottom = (byte)Math.Max(0, (int)(flashAlpha * 0.55));

                // Slightly cool white reads more like lightning than pure white.
                var flashBrush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop(Color.FromArgb(top, 245, 250, 255), 0.00),
                        new GradientStop(Color.FromArgb(bottom, 245, 250, 255), 1.00),
                    }
                };

                context.DrawRectangle(flashBrush, null, new Rect(0, 0, w, h));
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

    private struct FirePuff(double x, double y, double vx, double vy, double r, double alpha, double phase)
    {
        public double X = x;
        public double Y = y;
        public double Vx = vx;
        public double Vy = vy;
        public double R = r;
        public double Alpha = alpha;
        public double Phase = phase;

        public bool IsBaseLayer;

        public double Flicker;
        public double Age;

        public static FirePuff Create(Random rng, double w, double h, double baseLevel, bool baseLayer)
        {
            var r = 12 + (rng.NextDouble() * (22 + (18 * baseLevel)));
            var x = rng.NextDouble() * w;
            // Always spawn from the bottom edge. Intensity should only make the fire taller,
            // not shift its base upward.
            var baseBand = h * 0.09; // constant spawn thickness near the bottom
            var y = (h - (rng.NextDouble() * baseBand)) + (rng.NextDouble() * 28);

            var vigor = 0.85 + (0.95 * baseLevel);
            var vx = (-28 + rng.NextDouble() * 56) * vigor * (baseLayer ? 0.85 : 1.05);

            // Base layer stays lower; upper layer reaches higher (within the fire region).
            var vy = baseLayer
                ? -(34 + (rng.NextDouble() * (55 + (95 * baseLevel))))
                : -(50 + (rng.NextDouble() * (95 + (180 * baseLevel))));

            var a = 0.07 + (rng.NextDouble() * (0.11 + (0.12 * baseLevel)));
            var phase = rng.NextDouble() * Math.PI * 2.0;

            return new FirePuff(x, y, vx, vy, r, a, phase) { Flicker = 1.0, Age = 0, IsBaseLayer = baseLayer };
        }
    }

    private struct Ember(double x, double y, double vx, double vy, double r, double alpha, double heat, double phase)
    {
        public double X = x;
        public double Y = y;
        public double Vx = vx;
        public double Vy = vy;
        public double R = r;
        public double Alpha = alpha;
        public double Heat = heat;
        public double Phase = phase;

        public double Age;

        public static Ember Create(Random rng, double w, double h, double baseLevel)
        {
            var x = rng.NextDouble() * w;
            // Always originate from the bottom edge.
            var baseBand = h * 0.08;
            var y = h - (rng.NextDouble() * baseBand) + (rng.NextDouble() * 30);
            var vx = (-36 + rng.NextDouble() * 72) * (0.90 + (0.60 * baseLevel));
            var vy = -(65 + (rng.NextDouble() * (120 + (170 * baseLevel))));
            var r = 0.65 + (rng.NextDouble() * (1.15 + (0.75 * baseLevel)));
            var a = 0.09 + (rng.NextDouble() * (0.22 + (0.20 * baseLevel)));
            var heat = 0.55 + (rng.NextDouble() * 0.45);
            var phase = rng.NextDouble() * Math.PI * 2.0;

            return new Ember(x, y, vx, vy, r, a, heat, phase) { Age = 0 };
        }
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

            // Spawn slightly outside the visible bounds so cropped puffs soften the portal edges.
            var pad = baseR * 0.80;
            var x = (-pad) + (rng.NextDouble() * (w + (pad * 2)));
            var y = (-pad) + (rng.NextDouble() * (h + (pad * 2)));

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
