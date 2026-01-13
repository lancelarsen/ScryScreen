using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ScryScreen.App.Controls;

/// <summary>
/// Grid-based falling-sand hourglass simulation.
/// Non-overlap is guaranteed by construction: each grid cell can contain at most one particle.
/// </summary>
public sealed class HourglassSandVisual : Control
{
    public static readonly StyledProperty<double> FractionRemainingProperty =
        AvaloniaProperty.Register<HourglassSandVisual, double>(nameof(FractionRemaining), 1.0);

    public static readonly StyledProperty<bool> IsRunningProperty =
        AvaloniaProperty.Register<HourglassSandVisual, bool>(nameof(IsRunning), true);

    public static readonly StyledProperty<int> ParticleCountProperty =
        AvaloniaProperty.Register<HourglassSandVisual, int>(nameof(ParticleCount), 3000);

    /// <summary>
    /// Gravity as cells/s^2.
    /// </summary>
    public static readonly StyledProperty<double> GravityProperty =
        AvaloniaProperty.Register<HourglassSandVisual, double>(nameof(Gravity), 90.0);

    /// <summary>
    /// Density/weight (0..10). Higher density => less sideways slippage (steeper pile).
    /// </summary>
    public static readonly StyledProperty<double> DensityProperty =
        AvaloniaProperty.Register<HourglassSandVisual, double>(nameof(Density), 5.0);

    /// <summary>
    /// Particle size in pixels (grid cell size).
    /// </summary>
    public static readonly StyledProperty<double> ParticleSizeProperty =
        AvaloniaProperty.Register<HourglassSandVisual, double>(nameof(ParticleSize), 4.0);

    /// <summary>
    /// Maximum number of particles allowed through the neck per second.
    /// </summary>
    public static readonly StyledProperty<int> MaxReleasePerFrameProperty =
        AvaloniaProperty.Register<HourglassSandVisual, int>(nameof(MaxReleasePerFrame), 120);

    private readonly DispatcherTimer _renderTimer;
    private DateTime _lastTickUtc;

    private readonly Random _rng = new(0x53_41_4E_44);

    // Grid state
    private bool[]? _occ;
    private int _gridW;
    private int _gridH;
    private int[]? _rowMin;
    private int[]? _rowMax;
    private int _topStartRow;
    private int _neckStartRow;
    private int _neckEndRow;
    private int _bottomEndRow;
    private int _slotX;

    private double _lastW;
    private double _lastH;
    private double _lastParticleSize = 4.0;
    private int _lastParticleCount = 3000;
    private bool _needsReseed = true;

    // Time/flow
    private double _lastFrac = 1.0;
    private double _releaseCarry;
    private int _passBudget;
    private int _passedCount;

    // Motion integration (grid)
    private double _fallVelocity;
    private double _moveCarry;
    private int _stepParity;

    // Visual
    private static readonly IBrush SandBrush = new SolidColorBrush(Color.FromRgb(218, 162, 76));
    private static readonly IBrush GlassBrush = new LinearGradientBrush
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

    public HourglassSandVisual()
    {
        _renderTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, (_, _) => Tick());
        _lastTickUtc = DateTime.UtcNow;
        _renderTimer.Start();
    }

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

    public int ParticleCount
    {
        get => GetValue(ParticleCountProperty);
        set => SetValue(ParticleCountProperty, value);
    }

    public double Gravity
    {
        get => GetValue(GravityProperty);
        set => SetValue(GravityProperty, value);
    }

    public double Density
    {
        get => GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
    }

    public double ParticleSize
    {
        get => GetValue(ParticleSizeProperty);
        set => SetValue(ParticleSizeProperty, value);
    }

    public int MaxReleasePerFrame
    {
        get => GetValue(MaxReleasePerFrameProperty);
        set => SetValue(MaxReleasePerFrameProperty, value);
    }

    private static double Clamp(double v, double min, double max)
        => v < min ? min : (v > max ? max : v);

    private static int Clamp(int v, int min, int max)
        => v < min ? min : (v > max ? max : v);

    private double GetClampedFraction()
    {
        var frac = FractionRemaining;
        if (double.IsNaN(frac) || double.IsInfinity(frac)) frac = 0;
        if (frac < 0) frac = 0;
        if (frac > 1) frac = 1;
        return frac;
    }

    private void Tick()
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 4 || h <= 4)
        {
            return;
        }

        // If the timer is rewound (Reset), start the sand over.
        var fracNow = GetClampedFraction();
        if (!_needsReseed && fracNow > _lastFrac + 0.20)
        {
            _needsReseed = true;
        }

        var now = DateTime.UtcNow;
        var dt = now - _lastTickUtc;
        _lastTickUtc = now;

        // Guard clock jumps.
        if (dt < TimeSpan.Zero || dt > TimeSpan.FromSeconds(0.25))
        {
            dt = TimeSpan.FromMilliseconds(16);
        }

        EnsureGrid();

        if (IsRunning)
        {
            UpdateFlowAndSim(dt.TotalSeconds);
        }

        InvalidateVisual();
    }

    private void EnsureGrid()
    {
        var w = Bounds.Width;
        var h = Bounds.Height;

        var particleSize = Clamp(ParticleSize, 2.0, 14.0);
        var particleCount = Clamp(ParticleCount, 10, 200000);

        var sizeChanged = Math.Abs(w - _lastW) > 0.5 || Math.Abs(h - _lastH) > 0.5;
        var particleSizeChanged = Math.Abs(particleSize - _lastParticleSize) > 0.001;
        var particleCountChanged = particleCount != _lastParticleCount;

        if (!_needsReseed && !sizeChanged && !particleSizeChanged && !particleCountChanged)
        {
            return;
        }

        _lastW = w;
        _lastH = h;
        _lastParticleSize = particleSize;
        _lastParticleCount = particleCount;
        _needsReseed = false;

        BuildMaskAndGrid(w, h, particleSize);
        SeedTop(particleCount);

        // Reset flow integrators.
        _lastFrac = GetClampedFraction();
        _releaseCarry = 0;
        _passBudget = 0;
        _passedCount = 0;
        _fallVelocity = 0;
        _moveCarry = 0;
        _stepParity = 0;
    }

    private void BuildMaskAndGrid(double w, double h, double cell)
    {
        _gridW = Math.Max(8, (int)Math.Floor(w / cell));
        _gridH = Math.Max(8, (int)Math.Floor(h / cell));

        _occ = new bool[_gridW * _gridH];
        _rowMin = new int[_gridH];
        _rowMax = new int[_gridH];

        // Pixel geometry similar to the existing hourglass, but with a neck sized to one cell.
        var cxPx = w / 2.0;
        var m = Math.Max(6.0, Math.Min(w, h) * 0.06);
        var neckHPx = Math.Max(cell * 2.0, h * 0.10);
        var chamberHPx = (h - neckHPx - (m * 2.0)) / 2.0;
        if (chamberHPx <= cell * 2.0)
        {
            chamberHPx = Math.Max(cell * 2.0, (h - (m * 2.0)) / 2.0);
        }

        var topY0 = m;
        var topY1 = topY0 + chamberHPx;
        var neckY0 = topY1;
        var neckY1 = neckY0 + neckHPx;
        var botY0 = neckY1;
        var botY1 = botY0 + chamberHPx;

        // Slot is one cell wide, with a little visual clearance.
        var topHalfW = (w - (m * 2.0)) / 2.0;
        var neckHalfW = cell * 0.62;

        // Cache important rows.
        _topStartRow = Math.Clamp((int)Math.Floor(topY0 / cell), 0, _gridH - 1);
        _neckStartRow = Math.Clamp((int)Math.Floor(neckY0 / cell), 0, _gridH - 1);
        _neckEndRow = Math.Clamp((int)Math.Floor(neckY1 / cell), 0, _gridH - 1);
        _bottomEndRow = Math.Clamp((int)Math.Ceiling(botY1 / cell), 0, _gridH);

        _slotX = Math.Clamp((int)Math.Round(cxPx / cell), 0, _gridW - 1);

        for (var y = 0; y < _gridH; y++)
        {
            var yPx = (y + 0.5) * cell;

            if (yPx < topY0 || yPx > botY1)
            {
                _rowMin[y] = 1;
                _rowMax[y] = 0;
                continue;
            }

            if (yPx >= neckY0 && yPx <= neckY1)
            {
                // Neck: single slot.
                _rowMin[y] = _slotX;
                _rowMax[y] = _slotX;
                continue;
            }

            if (yPx < neckY0)
            {
                // Top chamber trapezoid.
                var t = (yPx - topY0) / Math.Max(1.0, topY1 - topY0);
                t = Clamp(t, 0, 1);
                var half = topHalfW + ((neckHalfW - topHalfW) * t);
                var minX = (int)Math.Floor((cxPx - half) / cell);
                var maxX = (int)Math.Ceiling((cxPx + half) / cell) - 1;
                minX = Math.Clamp(minX, 0, _gridW - 1);
                maxX = Math.Clamp(maxX, 0, _gridW - 1);
                _rowMin[y] = minX;
                _rowMax[y] = maxX;
                continue;
            }

            // Bottom chamber trapezoid.
            {
                var t = (yPx - botY0) / Math.Max(1.0, botY1 - botY0);
                t = Clamp(t, 0, 1);
                var half = neckHalfW + ((topHalfW - neckHalfW) * t);
                var minX = (int)Math.Floor((cxPx - half) / cell);
                var maxX = (int)Math.Ceiling((cxPx + half) / cell) - 1;
                minX = Math.Clamp(minX, 0, _gridW - 1);
                maxX = Math.Clamp(maxX, 0, _gridW - 1);
                _rowMin[y] = minX;
                _rowMax[y] = maxX;
            }
        }
    }

    private bool IsInside(int x, int y)
    {
        if (_rowMin is null || _rowMax is null) return false;
        if (y < 0 || y >= _gridH) return false;
        return x >= _rowMin[y] && x <= _rowMax[y];
    }

    private int Idx(int x, int y) => (y * _gridW) + x;

    private void ClearGrid()
    {
        if (_occ is null) return;
        Array.Clear(_occ, 0, _occ.Length);
    }

    private void SeedTop(int desiredCount)
    {
        if (_occ is null || _rowMin is null || _rowMax is null) return;

        ClearGrid();

        var count = 0;
        var maxCount = Math.Max(0, desiredCount);

        // Fill from the very top of the top chamber downward.
        for (var y = _topStartRow; y < _neckStartRow && count < maxCount; y++)
        {
            var minX = _rowMin[y];
            var maxX = _rowMax[y];
            if (minX > maxX) continue;

            for (var x = minX; x <= maxX && count < maxCount; x++)
            {
                _occ[Idx(x, y)] = true;
                count++;
            }
        }
    }

    private void UpdateFlowAndSim(double dtSeconds)
    {
        if (_occ is null || _rowMin is null || _rowMax is null) return;

        var frac = GetClampedFraction();

        // Convert timer progress into a target count of grains that should have passed the neck so far.
        // The timer updates at ~200ms, so FractionRemaining changes in chunks; to keep visuals smooth,
        // we release at a steady grains/sec rate (Flow) up to what the timer indicates.
        var total = Clamp(ParticleCount, 10, 200000);
        var targetPassed = (1.0 - frac) * total;
        var needed = targetPassed - _passedCount;

        if (needed > 0)
        {
            var flowPerSecond = Clamp(MaxReleasePerFrame, 1, 20000);
            var desired = (flowPerSecond * dtSeconds) + _releaseCarry;
            var toRelease = (int)Math.Floor(desired);
            _releaseCarry = desired - toRelease;

            // Don't exceed what the timer says should have passed.
            toRelease = Math.Min(toRelease, (int)Math.Floor(needed));

            // Guard rare big dt events.
            toRelease = Math.Min(toRelease, 2000);

            _passBudget += toRelease;
        }

        // Determine how many grid substeps to run this frame.
        var g = Clamp(Gravity, 1.0, 400.0);
        _fallVelocity += g * dtSeconds;
        var move = (_fallVelocity * dtSeconds) + _moveCarry;
        var steps = (int)Math.Floor(move);
        _moveCarry = move - steps;
        steps = Clamp(steps, 1, 6);

        var density = Clamp(Density, 0.0, 10.0);
        var slideChance = 1.0 - ((density / 10.0) * 0.75);

        for (var s = 0; s < steps; s++)
        {
            StepOnce(slideChance);
        }

        _lastFrac = frac;
    }

    private void StepOnce(double slideChance)
    {
        if (_occ is null || _rowMin is null || _rowMax is null) return;

        // Gate between top chamber and neck: only allow passage when budget > 0.
        var gateRow = Math.Max(0, _neckStartRow - 1);

        // Iterate bottom-up to avoid double-moving.
        for (var y = _gridH - 2; y >= 0; y--)
        {
            var minX = _rowMin[y];
            var maxX = _rowMax[y];
            if (minX > maxX) continue;

            // Parity flip to reduce left/right bias.
            var leftFirst = (((y + _stepParity) & 1) == 0);

            for (var x = minX; x <= maxX; x++)
            {
                var i = Idx(x, y);
                if (!_occ[i]) continue;

                var y1 = y + 1;
                if (y1 >= _gridH) continue;

                // Down
                if (TryMove(x, y, x, y1, gateRow))
                {
                    continue;
                }

                // If blocked, maybe slide.
                if (_rng.NextDouble() > slideChance)
                {
                    continue;
                }

                if (leftFirst)
                {
                    if (TryMove(x, y, x - 1, y1, gateRow)) continue;
                    if (TryMove(x, y, x + 1, y1, gateRow)) continue;
                }
                else
                {
                    if (TryMove(x, y, x + 1, y1, gateRow)) continue;
                    if (TryMove(x, y, x - 1, y1, gateRow)) continue;
                }
            }
        }

        _stepParity ^= 1;
    }

    private bool TryMove(int x0, int y0, int x1, int y1, int gateRow)
    {
        if (_occ is null) return false;

        if (!IsInside(x1, y1))
        {
            return false;
        }

        // Flow gating: only permit crossing into the neck when budget allows.
        if (y0 == gateRow && y1 == gateRow + 1)
        {
            if (_passBudget <= 0)
            {
                return false;
            }
        }

        var i0 = Idx(x0, y0);
        var i1 = Idx(x1, y1);
        if (_occ[i1])
        {
            return false;
        }

        _occ[i0] = false;
        _occ[i1] = true;

        if (y0 == gateRow && y1 == gateRow + 1)
        {
            _passBudget--;
            _passedCount++;
        }

        return true;
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

        var cell = Clamp(ParticleSize, 2.0, 14.0);

        // Draw glass outline (fantasy frame) with a neck that visually has slight clearance.
        var cx = w / 2.0;
        var m = Math.Max(6, Math.Min(w, h) * 0.06);
        var neckH = Math.Max(cell * 2.0, h * 0.10);
        var chamberH = (h - neckH - (m * 2)) / 2.0;
        if (chamberH <= cell * 2.0)
        {
            chamberH = Math.Max(cell * 2.0, (h - (m * 2)) / 2.0);
        }

        var topY0 = m;
        var topY1 = topY0 + chamberH;
        var neckY0 = topY1;
        var neckY1 = neckY0 + neckH;
        var botY0 = neckY1;
        var botY1 = botY0 + chamberH;

        var topW = w - (m * 2);
        var botW = topW;
        var neckW = Math.Max(cell * 1.45, w * 0.035);
        var curve = Math.Min(topW, chamberH) * 0.22;

        var outline = new StreamGeometry();
        using (var g = outline.Open())
        {
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
        context.DrawGeometry(GlassBrush, null, outline);

        // Sand
        EnsureGrid();
        if (_occ is not null)
        {
            var drawSize = Math.Max(1.0, cell * 0.92);
            using (context.PushGeometryClip(outline))
            {
                for (var y = 0; y < _gridH; y++)
                {
                    for (var x = 0; x < _gridW; x++)
                    {
                        if (!_occ[Idx(x, y)]) continue;
                        var px = x * cell;
                        var py = y * cell;
                        context.FillRectangle(SandBrush, new Rect(px + ((cell - drawSize) * 0.5), py + ((cell - drawSize) * 0.5), drawSize, drawSize));
                    }
                }
            }
        }

        // Highlights
        var highlight = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), thickness: Math.Max(1, Math.Min(w, h) * 0.01));
        context.DrawLine(highlight, new Point(cx - topW * 0.24, topY0 + chamberH * 0.08), new Point(cx - neckW * 0.55, neckY0 + neckH * 0.15));
        context.DrawLine(highlight, new Point(cx + topW * 0.26, topY0 + chamberH * 0.12), new Point(cx + neckW * 0.62, neckY0 + neckH * 0.25));
    }
}
