using System;
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
        OverlayOpacity = 0.85;
        EraserDiameter = 60;
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
    private double overlayOpacity;

    [ObservableProperty]
    private double eraserDiameter;

    [ObservableProperty]
    private MediaScaleMode previewScaleMode;

    [ObservableProperty]
    private MediaAlign previewAlign;

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

    public MapMasterOverlayState SnapshotState() => new(MaskBitmap, OverlayOpacity);

    public void SetPreviewSource(string? imagePath, MediaScaleMode scaleMode, MediaAlign align)
    {
        PreviewScaleMode = scaleMode;
        PreviewAlign = align;

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

    partial void OnOverlayOpacityChanged(double value)
    {
        if (OverlayOpacity < 0) OverlayOpacity = 0;
        if (OverlayOpacity > 1) OverlayOpacity = 1;
        StateChanged?.Invoke();
    }

    partial void OnEraserDiameterChanged(double value)
    {
        if (EraserDiameter < 2) EraserDiameter = 2;
        if (EraserDiameter > 300) EraserDiameter = 300;
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
                p[0] = 0;   // B
                p[1] = 0;   // G
                p[2] = 0;   // R
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
                if (dx * dx + dy2 > r2)
                {
                    continue;
                }

                var p = row + (x * 4);
                // Transparent pixel reveals the underlying portal content.
                p[0] = 0;
                p[1] = 0;
                p[2] = 0;
                p[3] = 0;
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
