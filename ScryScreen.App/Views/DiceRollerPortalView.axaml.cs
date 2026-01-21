using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private PortalWindowViewModel? _portalVm;
    private DiceTray3DHost? _tray;
    private long _lastRollRequestId;
    private long _lastClearDiceId;
    private long _lastVisualConfigRevision;

    private Bitmap? _lastBackdropImage;
    private string? _lastBackdropImageDataUrl;
    private string? _lastBackdropVideoPath;
    private MediaScaleMode _lastBackdropScaleMode;
    private MediaAlign _lastBackdropAlign;

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
                _tray.TrayReady += OnTrayReady;
            }
            HookPortalVm();
            HookVm();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            UnhookVm();
            UnhookPortalVm();
            if (_tray is not null)
            {
                _tray.DieRollCompleted -= OnTrayDieRollCompleted;
                _tray.TrayReady -= OnTrayReady;
            }
            _tray = null;
        };
        DataContextChanged += (_, _) => HookVm();
    }

    private void HookPortalVm()
    {
        UnhookPortalVm();

        // This view is hosted inside PortalWindow/PortalOverlayWindow; their DataContext is PortalWindowViewModel.
        _portalVm = TopLevel.GetTopLevel(this)?.DataContext as PortalWindowViewModel;
        if (_portalVm is not null)
        {
            _portalVm.PropertyChanged += OnPortalVmPropertyChanged;
            Dispatcher.UIThread.Post(ApplyBackdropToTray, DispatcherPriority.Render);
        }
    }

    private void UnhookPortalVm()
    {
        if (_portalVm is not null)
        {
            _portalVm.PropertyChanged -= OnPortalVmPropertyChanged;
        }

        _portalVm = null;
        _lastBackdropImage = null;
        _lastBackdropImageDataUrl = null;
        _lastBackdropVideoPath = null;
    }

    private void OnPortalVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_portalVm is null)
        {
            return;
        }

        if (e.PropertyName == nameof(PortalWindowViewModel.ContentImage)
            || e.PropertyName == nameof(PortalWindowViewModel.ContentVideoPath)
            || e.PropertyName == nameof(PortalWindowViewModel.IsShowingImage)
            || e.PropertyName == nameof(PortalWindowViewModel.IsShowingVideo)
            || e.PropertyName == nameof(PortalWindowViewModel.ScaleMode)
            || e.PropertyName == nameof(PortalWindowViewModel.Align))
        {
            Dispatcher.UIThread.Post(ApplyBackdropToTray, DispatcherPriority.Render);
        }
    }

    private void HookVm()
    {
        UnhookVm();

        _vm = DataContext as DiceRollerPortalViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            // Force a config push on initial attach.
            _lastVisualConfigRevision = long.MinValue;
            Dispatcher.UIThread.Post(ApplyVisualConfigToTray, DispatcherPriority.Render);
        }
    }

    private void OnTrayReady(object? sender, EventArgs e)
    {
        // JS side has attached its message listener; re-push config to avoid early-send race.
        if (_tray is null)
        {
            return;
        }

        // Force a resend of any pending state now that the tray can receive messages.
        _lastVisualConfigRevision = long.MinValue;
        _lastClearDiceId = 0;
        _lastRollRequestId = 0;

        Dispatcher.UIThread.Post(() =>
        {
            ApplyBackdropToTray();
            ApplyVisualConfigToTray();
            ApplyClearToTray();
            ApplyRollRequestToTray();
            ApplyRotationsToTray();
        }, DispatcherPriority.Render);
    }

    private void ApplyBackdropToTray()
    {
        if (_tray is null)
        {
            return;
        }

        if (!_tray.IsTrayReady)
        {
            return;
        }

        var portal = _portalVm;
        if (portal is null)
        {
            _tray.ClearBackdrop();
            return;
        }

        var scaleMode = portal.ScaleMode;
        var align = portal.Align;

        // Avoid resending if nothing relevant changed.
        var vidPath = portal.IsShowingVideo ? portal.ContentVideoPath : null;
        var img = portal.IsShowingImage ? portal.ContentImage : null;

        if (vidPath is not null)
        {
            if (string.Equals(_lastBackdropVideoPath, vidPath, StringComparison.Ordinal)
                && _lastBackdropScaleMode == scaleMode
                && _lastBackdropAlign == align)
            {
                return;
            }

            _lastBackdropVideoPath = vidPath;
            _lastBackdropImage = null;
            _lastBackdropImageDataUrl = null;
            _lastBackdropScaleMode = scaleMode;
            _lastBackdropAlign = align;

            var uri = new Uri(vidPath, UriKind.Absolute);
            _tray.SetBackdrop(
                kind: "video",
                src: uri.AbsoluteUri,
                scaleMode: scaleMode == MediaScaleMode.FillWidth ? "fillWidth" : "fillHeight",
                align: align.ToString().ToLowerInvariant(),
                loop: false,
                muted: true);
            return;
        }

        if (img is not null)
        {
            if (ReferenceEquals(_lastBackdropImage, img)
                && _lastBackdropScaleMode == scaleMode
                && _lastBackdropAlign == align
                && !string.IsNullOrWhiteSpace(_lastBackdropImageDataUrl))
            {
                return;
            }

            _lastBackdropVideoPath = null;
            _lastBackdropScaleMode = scaleMode;
            _lastBackdropAlign = align;

            if (!ReferenceEquals(_lastBackdropImage, img) || string.IsNullOrWhiteSpace(_lastBackdropImageDataUrl))
            {
                _lastBackdropImage = img;
                _lastBackdropImageDataUrl = TryEncodePngDataUrl(img);
            }

            if (string.IsNullOrWhiteSpace(_lastBackdropImageDataUrl))
            {
                _tray.ClearBackdrop();
                return;
            }

            _tray.SetBackdrop(
                kind: "image",
                src: _lastBackdropImageDataUrl,
                scaleMode: scaleMode == MediaScaleMode.FillWidth ? "fillWidth" : "fillHeight",
                align: align.ToString().ToLowerInvariant(),
                loop: false,
                muted: true);
            return;
        }

        if (_lastBackdropVideoPath is null && _lastBackdropImage is null)
        {
            return;
        }

        _lastBackdropVideoPath = null;
        _lastBackdropImage = null;
        _lastBackdropImageDataUrl = null;
        _tray.ClearBackdrop();
    }

    private static string? TryEncodePngDataUrl(Bitmap bitmap)
    {
        try
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
            {
                return null;
            }
            return "data:image/png;base64," + Convert.ToBase64String(bytes);
        }
        catch
        {
            return null;
        }
    }

    private void UnhookVm()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = null;
        _lastVisualConfigRevision = 0;

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

        if (e.PropertyName == nameof(DiceRollerPortalViewModel.VisualConfigRevision) && _vm is not null)
        {
            Dispatcher.UIThread.Post(ApplyVisualConfigToTray, DispatcherPriority.Render);
        }
    }

    private void ApplyVisualConfigToTray()
    {
        if (_tray is null || _vm is null)
        {
            return;
        }

        if (!_tray.IsTrayReady)
        {
            // Avoid sending config before JS is ready (it can be dropped, causing default-size flicker).
            return;
        }

        if (_vm.VisualConfigRevision == _lastVisualConfigRevision)
        {
            return;
        }

        _lastVisualConfigRevision = _vm.VisualConfigRevision;

        var configs = _vm.VisualConfigs;
        if (configs is null || configs.Count == 0)
        {
            return;
        }

        _tray.SetDiceVisualConfig(configs, _vm.VisualConfigRevision);
    }

    private void ApplyClearToTray()
    {
        if (_tray is null || _vm is null)
        {
            return;
        }

        if (!_tray.IsTrayReady)
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

        if (!_tray.IsTrayReady)
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

        if (!_tray.IsTrayReady)
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
