using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScryScreen.App.Models;

namespace ScryScreen.App.ViewModels;

public partial class MapMasterViewModel : ViewModelBase
{
    public IReadOnlyList<MapMasterMaskType> MaskTypes { get; } =
    [
        MapMasterMaskType.Black,
        MapMasterMaskType.Dirt,
        MapMasterMaskType.FogNight,
        MapMasterMaskType.Fog,
        MapMasterMaskType.Rock1,
        MapMasterMaskType.Rock2,
    ];

    public event Action? StateChanged;

    private const int MaskChangeNotifyThrottleMs = 33;
    private long _lastMaskChangeNotifyTickMs;

    private const int DefaultMaskW = 1024;
    private const int DefaultMaskH = 576;

    private readonly WriteableBitmap _maskA;
    private readonly WriteableBitmap _maskB;
    private bool _useMaskA = true;

    private Bitmap? _sourceImage;
    private string? _sourceImagePath;

    public MapMasterViewModel()
    {
        PlayerMaskOpacity = 1.0;
        GmMaskOpacity = 0.75;
        EraserDiameter = 50;
        EraserHardness = 0.0;
        SelectedMaskType = MapMasterMaskType.Black;
        PreviewScaleMode = MediaScaleMode.FillHeight;
        PreviewAlign = MediaAlign.Center;

        _maskA = new WriteableBitmap(
            new PixelSize(DefaultMaskW, DefaultMaskH),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);

        _maskB = new WriteableBitmap(
            new PixelSize(DefaultMaskW, DefaultMaskH),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);

        MaskBitmap = _maskA;
        FillMaskOpaque();
    }

    [ObservableProperty]
    private double playerMaskOpacity;

    [ObservableProperty]
    private double gmMaskOpacity;

    [ObservableProperty]
    private double eraserDiameter;

    [ObservableProperty]
    private double eraserHardness;

    [ObservableProperty]
    private MapMasterMaskType selectedMaskType;

    public bool IsSolidBlackMask => SelectedMaskType == MapMasterMaskType.Black;

    public Avalonia.Media.IImage? MaskTexture => MapMasterMaskAssets.GetTexture(SelectedMaskType);

    [ObservableProperty]
    private MediaScaleMode previewScaleMode;

    [ObservableProperty]
    private MediaAlign previewAlign;

    [ObservableProperty]
    private double previewPortalAspectRatio = 16.0 / 9.0;

    [ObservableProperty]
    private WriteableBitmap maskBitmap;

    public Bitmap? SourceImage
    {
        get => _sourceImage;
        private set
        {
            if (ReferenceEquals(_sourceImage, value))
            {
                return;
            }

            _sourceImage?.Dispose();
            _sourceImage = value;
            OnPropertyChanged(nameof(SourceImage));
            OnPropertyChanged(nameof(HasSourceImage));
        }
    }

    public bool HasSourceImage => SourceImage is not null;

    public MapMasterOverlayState SnapshotState() => new(MaskBitmap, PlayerMaskOpacity, SelectedMaskType);

    public void SetPreviewSource(string? imagePath, MediaScaleMode scaleMode, MediaAlign align, double portalAspectRatio)
    {
        PreviewScaleMode = scaleMode;
        PreviewAlign = align;
        if (portalAspectRatio > 0 && !double.IsNaN(portalAspectRatio) && !double.IsInfinity(portalAspectRatio))
        {
            PreviewPortalAspectRatio = portalAspectRatio;
        }

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            if (!string.IsNullOrWhiteSpace(_sourceImagePath))
            {
                _sourceImagePath = null;
                SourceImage = null;
            }
            return;
        }

        if (string.Equals(_sourceImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(imagePath);
            SourceImage = new Bitmap(stream);
            _sourceImagePath = imagePath;
        }
        catch
        {
            _sourceImagePath = null;
            SourceImage = null;
        }
    }

    [RelayCommand]
    private void ResetRevealToDefault()
    {
        FillMaskOpaque();
    }

    public void EraseAtSurfacePoint(Point surfacePoint, Size surfaceSize)
    {
        if (surfaceSize.Width <= 0 || surfaceSize.Height <= 0)
        {
            return;
        }

        var w = CurrentMask.PixelSize.Width;
        var h = CurrentMask.PixelSize.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var x = (int)Math.Round(surfacePoint.X / surfaceSize.Width * w);
        var y = (int)Math.Round(surfacePoint.Y / surfaceSize.Height * h);

        var scale = Math.Min(w / surfaceSize.Width, h / surfaceSize.Height);
        var radius = (int)Math.Round((EraserDiameter * 0.5) * scale);
        radius = Math.Max(radius, 1);

        EraseCircleAndSwapBuffers(x, y, radius);
    }

    public void CommitMaskEdits()
    {
        // We throttle during pointer-drag to keep things smooth, but we always want the
        // portals to receive the final mask state when the user finishes a stroke.
        _lastMaskChangeNotifyTickMs = Environment.TickCount64;
        StateChanged?.Invoke();
    }

    partial void OnPlayerMaskOpacityChanged(double value)
    {
        if (PlayerMaskOpacity < 0) PlayerMaskOpacity = 0;
        if (PlayerMaskOpacity > 1) PlayerMaskOpacity = 1;
        StateChanged?.Invoke();
    }

    partial void OnGmMaskOpacityChanged(double value)
    {
        if (GmMaskOpacity < 0) GmMaskOpacity = 0;
        if (GmMaskOpacity > 1) GmMaskOpacity = 1;
    }

    partial void OnEraserDiameterChanged(double value)
    {
        if (EraserDiameter < 2) EraserDiameter = 2;
        if (EraserDiameter > 80) EraserDiameter = 80;
    }

    partial void OnEraserHardnessChanged(double value)
    {
        if (EraserHardness < 0) EraserHardness = 0;
        if (EraserHardness > 1) EraserHardness = 1;
    }

    partial void OnSelectedMaskTypeChanged(MapMasterMaskType value)
    {
        OnPropertyChanged(nameof(IsSolidBlackMask));
        OnPropertyChanged(nameof(MaskTexture));
        StateChanged?.Invoke();
    }

    private unsafe void FillMaskOpaque()
    {
        // Write to the back buffer and then swap the displayed mask reference.
        using var fb = BackBuffer.Lock();
        var ptr = (byte*)fb.Address;
        if (ptr == null)
        {
            return;
        }

        var w = fb.Size.Width;
        var h = fb.Size.Height;
        var stride = fb.RowBytes;

        for (var y = 0; y < h; y++)
        {
            var row = ptr + (y * stride);
            for (var x = 0; x < w; x++)
            {
                var p = row + (x * 4);
                // MaskBitmap is primarily used as an alpha mask; keep RGB stable.
                p[0] = 255;
                p[1] = 255;
                p[2] = 255;
                p[3] = 255; // A
            }
        }

        SwapBuffers();
        StateChanged?.Invoke();
    }


    private WriteableBitmap CurrentMask => _useMaskA ? _maskA : _maskB;

    private WriteableBitmap BackBuffer => _useMaskA ? _maskB : _maskA;

    private void SwapBuffers()
    {
        _useMaskA = !_useMaskA;
        MaskBitmap = CurrentMask;
    }

    private unsafe void EraseCircleAndSwapBuffers(int centerX, int centerY, int radius)
    {
        // Copy current mask into back buffer, apply erase to back buffer, then swap.
        using var srcFb = CurrentMask.Lock();
        using var dstFb = BackBuffer.Lock();

        var srcPtr = (byte*)srcFb.Address;
        var dstPtr = (byte*)dstFb.Address;
        if (srcPtr == null || dstPtr == null)
        {
            return;
        }

        var w = Math.Min(srcFb.Size.Width, dstFb.Size.Width);
        var h = Math.Min(srcFb.Size.Height, dstFb.Size.Height);
        var srcStride = srcFb.RowBytes;
        var dstStride = dstFb.RowBytes;

        var rowBytesToCopy = Math.Min(srcStride, dstStride);
        for (var y = 0; y < h; y++)
        {
            Buffer.MemoryCopy(
                source: srcPtr + (y * srcStride),
                destination: dstPtr + (y * dstStride),
                destinationSizeInBytes: rowBytesToCopy,
                sourceBytesToCopy: rowBytesToCopy);
        }

        var r2 = radius * radius;
        var hardness = EraserHardness;
        if (hardness < 0) hardness = 0;
        if (hardness > 1) hardness = 1;
        var inner = (int)Math.Round(radius * hardness);
        if (inner < 0) inner = 0;
        if (inner > radius) inner = radius;
        var inner2 = inner * inner;
        var minY = Math.Max(0, centerY - radius);
        var maxY = Math.Min(h - 1, centerY + radius);
        var minXDirty = Math.Max(0, centerX - radius);
        var maxXDirty = Math.Min(w - 1, centerX + radius);

        for (var y = minY; y <= maxY; y++)
        {
            var dy = y - centerY;
            var dy2 = dy * dy;
            var row = dstPtr + (y * dstStride);

            for (var x = minXDirty; x <= maxXDirty; x++)
            {
                var dx = x - centerX;
                var d2 = dx * dx + dy2;
                if (d2 > r2)
                {
                    continue;
                }

                var p = row + (x * 4);
                // Soft erase: decrease alpha with a smooth falloff; never increase alpha.
                byte targetA;
                if (inner >= radius)
                {
                    targetA = 0;
                }
                else if (d2 <= inner2)
                {
                    targetA = 0;
                }
                else
                {
                    var d = Math.Sqrt(d2);
                    var t = (d - inner) / (radius - inner);
                    if (t < 0) t = 0;
                    if (t > 1) t = 1;
                    // Smoothstep
                    t = t * t * (3 - 2 * t);
                    targetA = (byte)Math.Round(255 * t);
                }

                if (targetA < p[3])
                {
                    p[3] = targetA;
                }
            }
        }

        SwapBuffers();
        NotifyMaskChanged();
    }

    private void NotifyMaskChanged()
    {
        // Update selected portals, but throttle to keep pointer-dragging smooth.
        var now = Environment.TickCount64;
        if (now - _lastMaskChangeNotifyTickMs < MaskChangeNotifyThrottleMs)
        {
            return;
        }

        _lastMaskChangeNotifyTickMs = now;
        StateChanged?.Invoke();
    }

}
