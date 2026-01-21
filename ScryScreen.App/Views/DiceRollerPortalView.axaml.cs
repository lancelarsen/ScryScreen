using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ScryScreen.App.Controls;
using ScryScreen.App.Services;
using ScryScreen.App.Utilities;
using ScryScreen.App.ViewModels;

namespace ScryScreen.App.Views;

public partial class DiceRollerPortalView : UserControl
{
    private const double DieBaseSize = 54;
    private const double StageWidth = 900;
    private const double StageHeight = 520;

    private sealed class DieVisual
    {
        public required Dice3DVisual Root { get; init; }
        public required ScaleTransform Scale { get; init; }
        public required SkewTransform Skew { get; init; }
        public required RotateTransform Rotate { get; init; }
    }

    private sealed class DieAnim
    {
        public required DieVisual Visual { get; init; }
        public required double DelaySeconds { get; init; }
        public required double DurationSeconds { get; init; }
        public required double StartX { get; init; }
        public required double EndX { get; init; }
        public required double BaseY { get; init; }
        public required double ArcHeight { get; init; }
        public required double SpinTurns { get; init; }
        public required double StartRot { get; init; }
        public required double Scale { get; init; }
    }

    private readonly List<DieAnim> _anims = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly Random _rng = new();

    private double _animWidth;
    private double _animHeight;

    private DiceRollerPortalViewModel? _vm;
    private DiceTray3DHost? _tray;
    private long _lastRollRequestId;
    private long _lastClearDiceId;

    public DiceRollerPortalView()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();

        AttachedToVisualTree += (_, _) =>
        {
            _tray = this.FindControl<DiceTray3DHost>("PortalDiceTray");
            if (_tray is not null)
            {
                _tray.DieRollCompleted += OnTrayDieRollCompleted;
            }
            HookVm();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            UnhookVm();
            if (_tray is not null)
            {
                _tray.DieRollCompleted -= OnTrayDieRollCompleted;
            }
            _tray = null;
        };
        DataContextChanged += (_, _) => HookVm();
    }

    private void HookVm()
    {
        UnhookVm();

        _vm = DataContext as DiceRollerPortalViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void UnhookVm()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = null;

        _timer.Stop();
        DiceCanvas.Children.Clear();
        _anims.Clear();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiceRollerPortalViewModel.Text) && _vm is not null)
        {
            // Text panel updates should not drive tray rolls.
            Dispatcher.UIThread.Post(() => StartRollAnimation(_vm.Text), DispatcherPriority.Render);
        }

        if (e.PropertyName == nameof(DiceRollerPortalViewModel.Rotations) && _vm is not null)
        {
            Dispatcher.UIThread.Post(ApplyRotationsToTray, DispatcherPriority.Render);
        }

        if (e.PropertyName == nameof(DiceRollerPortalViewModel.RollRequest) && _vm is not null)
        {
            Dispatcher.UIThread.Post(ApplyRollRequestToTray, DispatcherPriority.Render);
        }

        if (e.PropertyName == nameof(DiceRollerPortalViewModel.ClearDiceId) && _vm is not null)
        {
            Dispatcher.UIThread.Post(ApplyClearToTray, DispatcherPriority.Render);
        }
    }

    private void ApplyClearToTray()
    {
        if (_tray is null || _vm is null)
        {
            return;
        }

        if (_vm.ClearDiceId == _lastClearDiceId)
        {
            return;
        }

        _lastClearDiceId = _vm.ClearDiceId;
        _tray.ClearAllDice();
    }

    private void ApplyRollRequestToTray()
    {
        if (_tray is null || _vm is null)
        {
            return;
        }

        var req = _vm.RollRequest;
        if (req is null)
        {
            return;
        }

        if (req.RequestId == _lastRollRequestId)
        {
            return;
        }

        _lastRollRequestId = req.RequestId;
        _tray.RequestRandomRoll(req.RequestId, req.Sides, req.Direction);
    }

    private void OnTrayDieRollCompleted(object? sender, DiceTray3DHost.DieRollCompletedEventArgs e)
    {
        DiceRollerEventBus.RaiseSingleDieRollCompleted(e.RequestId, e.Sides, e.Value);
    }

    private void ApplyRotationsToTray()
    {
        if (_tray is null || _vm is null)
        {
            return;
        }

        foreach (var r in _vm.Rotations)
        {
            var q = new System.Numerics.Quaternion(r.X, r.Y, r.Z, r.W);
            _tray.SetDieRotation(r.Sides, q);
        }
    }

    private void StartRollAnimation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _timer.Stop();
            DiceCanvas.Children.Clear();
            _anims.Clear();
            _tray?.ClearAllDice();
            return;
        }

        // If the native 3D tray is present, it is driven by explicit RollRequest messages.
        // Do not parse Text and re-trigger rolls from it (that causes rerolls/snapbacks).
        if (_tray is not null)
        {
            ApplyRotationsToTray();
            ApplyRollRequestToTray();
            _timer.Stop();
            DiceCanvas.Children.Clear();
            _anims.Clear();
            if (DiceStage is not null)
            {
                DiceStage.IsVisible = false;
            }
            return;
        }

        // Parse rolled dice values from the display string. Example: "2d6(3,5) + 1 = 9".
        var dice = DiceRollTextParser.ParseDice(text);
        if (dice.Count == 0)
        {
            return;
        }

        // Use the fixed centered stage size; fall back to constants if Bounds aren't ready yet.
        var width = DiceCanvas.Bounds.Width > 1 ? DiceCanvas.Bounds.Width : StageWidth;
        var height = DiceCanvas.Bounds.Height > 1 ? DiceCanvas.Bounds.Height : StageHeight;
        width = Math.Max(200, width);
        height = Math.Max(200, height);

        _animWidth = width;
        _animHeight = height;

        // Keep it visually readable: cap at 20 dice animated.
        if (dice.Count > 20)
        {
            dice.RemoveRange(20, dice.Count - 20);
        }

        DiceCanvas.Children.Clear();
        _anims.Clear();

        const double padding = 24;
        var maxScale = 1.25;
        var dieSize = DieBaseSize * maxScale;

        var minX = padding;
        var maxX = Math.Max(minX + 1, width - dieSize - padding);

        // Keep motion centered instead of spanning the whole screen.
        var corridorLeft = Math.Max(minX, width * 0.25);
        var corridorRight = Math.Min(maxX, width * 0.75);
        if (corridorRight - corridorLeft < 40)
        {
            corridorLeft = minX;
            corridorRight = maxX;
        }

        // Center-ish band so dice don't spawn in the upper-left and arcs don't go negative.
        var bandHeight = Math.Min(260, height * 0.42);
        var bandCenter = height * 0.58;
        var laneTop = Math.Max(padding + dieSize * 0.5, bandCenter - bandHeight * 0.5);
        var laneBottom = Math.Min(height - padding - dieSize * 0.5, bandCenter + bandHeight * 0.5);
        var total = dice.Count;
        var baseDelay = 0.05;
        var duration = 1.35;

        for (var i = 0; i < total; i++)
        {
            var (sides, value) = dice[i];

            var visual = CreateDieVisual(value, sides);
            visual.Root.Opacity = 0;
            DiceCanvas.Children.Add(visual.Root);

            var y = laneTop + (laneBottom - laneTop) * _rng.NextDouble();
            var arc = 18 + 42 * _rng.NextDouble();
            var spin = 2.5 + 4.0 * _rng.NextDouble();
            var startRot = _rng.NextDouble() * 360.0;
            var scale = 0.9 + 0.35 * _rng.NextDouble();

            var leftToRight = _rng.NextDouble() >= 0.5;
            var startX = corridorLeft + (corridorRight - corridorLeft) * _rng.NextDouble();
            var endX = corridorLeft + (corridorRight - corridorLeft) * _rng.NextDouble();
            if (leftToRight && endX <= startX)
            {
                (startX, endX) = (endX, startX);
            }
            if (!leftToRight && endX >= startX)
            {
                (startX, endX) = (endX, startX);
            }

            _anims.Add(new DieAnim
            {
                Visual = visual,
                DelaySeconds = i * baseDelay,
                DurationSeconds = duration,
                StartX = startX,
                EndX = endX,
                BaseY = y,
                ArcHeight = arc,
                SpinTurns = spin,
                StartRot = startRot,
                Scale = scale,
            });
        }

        _clock.Restart();
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void Tick()
    {
        var t = _clock.Elapsed.TotalSeconds;

        var anyAlive = false;
        foreach (var anim in _anims)
        {
            var local = t - anim.DelaySeconds;
            if (local < 0)
            {
                anyAlive = true;
                continue;
            }

            var u = local / anim.DurationSeconds;
            if (u >= 1.0)
            {
                // Lock in the final pose and keep it visible until the next roll (or Clear).
                var finalX = Math.Clamp(anim.EndX, 0, Math.Max(0, _animWidth - DieBaseSize));
                var finalY = Math.Clamp(anim.BaseY, 0, Math.Max(0, _animHeight - DieBaseSize));
                Canvas.SetLeft(anim.Visual.Root, finalX);
                Canvas.SetTop(anim.Visual.Root, finalY);
                anim.Visual.Rotate.Angle = anim.StartRot + (anim.SpinTurns * 360.0);
                anim.Visual.Scale.ScaleX = anim.Scale;
                anim.Visual.Scale.ScaleY = anim.Scale;
                anim.Visual.Skew.AngleX = 0;
                anim.Visual.Skew.AngleY = 0;
                anim.Visual.Root.TiltX = 0;
                anim.Visual.Root.TiltY = 0;
                anim.Visual.Root.Opacity = 1;
                continue;
            }

            anyAlive = true;

            // Smoothstep for nicer motion
            var s = u * u * (3 - 2 * u);
            var x = Lerp(anim.StartX, anim.EndX, s);
            var y = anim.BaseY - anim.ArcHeight * Math.Sin(Math.PI * s) + 6 * Math.Sin(10 * s + anim.StartRot);

            // Clamp to avoid any tiny overshoot/jitter going off-screen.
            x = Math.Clamp(x, 0, Math.Max(0, _animWidth - DieBaseSize));
            y = Math.Clamp(y, 0, Math.Max(0, _animHeight - DieBaseSize));

            Canvas.SetLeft(anim.Visual.Root, x);
            Canvas.SetTop(anim.Visual.Root, y);

            // 3-axis tumble illusion: skew + shading tilt + spin.
            var spinPhase = (s * Math.PI * 2.0 * anim.SpinTurns) + (anim.StartRot * Math.PI / 180.0);
            var tiltX = Math.Sin(spinPhase);
            var tiltY = Math.Cos(spinPhase * 0.85);

            anim.Visual.Root.TiltX = tiltX;
            anim.Visual.Root.TiltY = tiltY;

            anim.Visual.Skew.AngleX = tiltX * 18;
            anim.Visual.Skew.AngleY = tiltY * -12;

            anim.Visual.Rotate.Angle = anim.StartRot + (anim.SpinTurns * 360.0 * s);

            var scale = anim.Scale * (0.92 + 0.10 * Math.Sin(Math.PI * s));
            var squash = 1.0 - (Math.Abs(tiltY) * 0.16);
            anim.Visual.Scale.ScaleX = scale;
            anim.Visual.Scale.ScaleY = scale * squash;

            // Fade in quickly, then stay visible (no auto-fade).
            var fadeIn = Math.Clamp(u / 0.12, 0, 1);
            anim.Visual.Root.Opacity = fadeIn;
        }

        if (!anyAlive)
        {
            _timer.Stop();
            _anims.Clear();
        }
    }

    private static DieVisual CreateDieVisual(int value, int sides)
    {
        var scale = new ScaleTransform(1, 1);
        var skew = new SkewTransform(0, 0);
        var rotate = new RotateTransform(0);
        var transforms = new TransformGroup();
        transforms.Children.Add(scale);
        transforms.Children.Add(skew);
        transforms.Children.Add(rotate);

        var root = new Dice3DVisual
        {
            Width = DieBaseSize,
            Height = DieBaseSize,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = transforms,
            Sides = sides,
            Value = value,
            TiltX = 0,
            TiltY = 0,
        };

        return new DieVisual { Root = root, Scale = scale, Skew = skew, Rotate = rotate };
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

}
