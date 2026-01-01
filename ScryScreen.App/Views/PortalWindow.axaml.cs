using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using ScryScreen.App.ViewModels;

namespace ScryScreen.App.Views;

public partial class PortalWindow : Window
{
    private PortalWindowViewModel? _vm;

    private double _lastHostW = double.NaN;
    private double _lastHostH = double.NaN;
    private MediaScaleMode _lastScaleMode;
    private MediaAlign _lastAlign;
    private bool _lastIsShowingImage;
    private object? _lastImageRef;

    private double _lastImageW = double.NaN;
    private double _lastImageH = double.NaN;
    private double _lastLeft = double.NaN;
    private double _lastTop = double.NaN;

    private double _lastVideoHostW = double.NaN;
    private double _lastVideoHostH = double.NaN;
    private int _lastVideoPxW;
    private int _lastVideoPxH;
    private bool _lastIsShowingVideo;
    private bool _lastVideoSizeUnknown;
    private double _lastVideoW = double.NaN;
    private double _lastVideoH = double.NaN;
    private double _lastVideoLeft = double.NaN;
    private double _lastVideoTop = double.NaN;

    public PortalWindow()
    {
        InitializeComponent();

        // Keep image layout in sync with fullscreen transitions, DPI scaling, etc.
        SizeChanged += (_, _) => UpdateImageLayout();
        ImageHost.SizeChanged += (_, _) => UpdateImageLayout();
        VideoHost.SizeChanged += (_, _) => UpdateVideoLayout();
        DataContextChanged += (_, _) => HookViewModel();

        // Ensure the native video surface exists before priming paused frames.
        ContentVideo.AttachedToVisualTree += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateVideoLayout();
                if (DataContext is PortalWindowViewModel vm)
                {
                    vm.PrimePausedFrameIfNeeded();
                }
            }, DispatcherPriority.Background);
        };
    }

    private void HookViewModel()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as PortalWindowViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateImageLayout();
        UpdateVideoLayout();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PortalWindowViewModel.ContentImage) or nameof(PortalWindowViewModel.ScaleMode) or nameof(PortalWindowViewModel.Align) or nameof(PortalWindowViewModel.IsShowingImage))
        {
            UpdateImageLayout();
        }

        if (e.PropertyName is nameof(PortalWindowViewModel.ContentVideoPath) or nameof(PortalWindowViewModel.ScaleMode) or nameof(PortalWindowViewModel.Align) or nameof(PortalWindowViewModel.IsShowingVideo))
        {
            UpdateVideoLayout();

            if (DataContext is PortalWindowViewModel vm && vm.IsShowingVideo)
            {
                // Let the layout/visual tree settle so VideoView can attach its rendering target.
                Dispatcher.UIThread.Post(() =>
                {
                    vm.PrimePausedFrameIfNeeded();
                }, DispatcherPriority.Background);
            }
        }
    }

    private void UpdateImageLayout()
    {
        if (ContentImage is null || ImageHost is null)
        {
            return;
        }

        if (DataContext is not PortalWindowViewModel vm || !vm.IsShowingImage || vm.ContentImage is null)
        {
            if (!double.IsNaN(ContentImage.Width)) ContentImage.Width = double.NaN;
            if (!double.IsNaN(ContentImage.Height)) ContentImage.Height = double.NaN;
            Canvas.SetLeft(ContentImage, 0);
            Canvas.SetTop(ContentImage, 0);

            _lastHostW = double.NaN;
            _lastHostH = double.NaN;
            _lastIsShowingImage = false;
            _lastImageRef = null;

            _lastImageW = double.NaN;
            _lastImageH = double.NaN;
            _lastLeft = double.NaN;
            _lastTop = double.NaN;
            return;
        }

        var hostW = ImageHost.Bounds.Width;
        var hostH = ImageHost.Bounds.Height;

        if (hostW <= 0 || hostH <= 0)
        {
            return;
        }

        var pxW = vm.ContentImage.PixelSize.Width;
        var pxH = vm.ContentImage.PixelSize.Height;
        if (pxW <= 0 || pxH <= 0)
        {
            return;
        }

        // If nothing relevant changed since the last call, do nothing.
        // This is critical to avoid Avalonia's "Infinite layout loop" detection.
        if (_lastIsShowingImage == vm.IsShowingImage &&
            _lastImageRef == vm.ContentImage &&
            _lastScaleMode == vm.ScaleMode &&
            _lastAlign == vm.Align &&
            Math.Abs(_lastHostW - hostW) < 0.01 &&
            Math.Abs(_lastHostH - hostH) < 0.01)
        {
            return;
        }

        _lastIsShowingImage = vm.IsShowingImage;
        _lastImageRef = vm.ContentImage;
        _lastScaleMode = vm.ScaleMode;
        _lastAlign = vm.Align;
        _lastHostW = hostW;
        _lastHostH = hostH;

        // NOTE: To match the requested UI:
        // - Fill Height (H) => scale to fill HEIGHT, crop horizontally (Left/Center/Right)
        // - Fill Width  (W) => scale to fill WIDTH, crop vertically (Top/Center/Bottom)
        var scale = vm.ScaleMode == MediaScaleMode.FillHeight
            ? hostH / pxH
            : hostW / pxW;

        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            return;

        var imageW = pxW * scale;
        var imageH = pxH * scale;

        // Center on the non-alignment axis.
        var left = (hostW - imageW) * 0.5;
        var top = (hostH - imageH) * 0.5;

        var a = vm.Align switch
        {
            MediaAlign.Start => 0.0,
            MediaAlign.Center => 0.5,
            MediaAlign.End => 1.0,
            _ => 0.5,
        };

        if (vm.ScaleMode == MediaScaleMode.FillHeight)
        {
            // Horizontal alignment (L/C/R)
            left = (hostW - imageW) * a;
        }
        else
        {
            // Vertical alignment (T/C/B)
            top = (hostH - imageH) * a;
        }

        // Only set if changed to avoid unnecessary layout churn.
        if (double.IsNaN(_lastImageW) || Math.Abs(_lastImageW - imageW) > 0.01)
        {
            ContentImage.Width = imageW;
            _lastImageW = imageW;
        }

        if (double.IsNaN(_lastImageH) || Math.Abs(_lastImageH - imageH) > 0.01)
        {
            ContentImage.Height = imageH;
            _lastImageH = imageH;
        }

        if (double.IsNaN(_lastLeft) || Math.Abs(_lastLeft - left) > 0.01)
        {
            Canvas.SetLeft(ContentImage, left);
            _lastLeft = left;
        }

        if (double.IsNaN(_lastTop) || Math.Abs(_lastTop - top) > 0.01)
        {
            Canvas.SetTop(ContentImage, top);
            _lastTop = top;
        }
    }

    private void UpdateVideoLayout()
    {
        if (ContentVideo is null || VideoHost is null)
        {
            return;
        }

        if (DataContext is not PortalWindowViewModel vm || !vm.IsShowingVideo)
        {
            if (!double.IsNaN(ContentVideo.Width)) ContentVideo.Width = double.NaN;
            if (!double.IsNaN(ContentVideo.Height)) ContentVideo.Height = double.NaN;
            Canvas.SetLeft(ContentVideo, 0);
            Canvas.SetTop(ContentVideo, 0);

            _lastIsShowingVideo = false;
            _lastVideoSizeUnknown = false;
            _lastVideoHostW = double.NaN;
            _lastVideoHostH = double.NaN;
            _lastVideoPxW = 0;
            _lastVideoPxH = 0;
            _lastVideoW = double.NaN;
            _lastVideoH = double.NaN;
            _lastVideoLeft = double.NaN;
            _lastVideoTop = double.NaN;
            return;
        }

        var hostW = VideoHost.Bounds.Width;
        var hostH = VideoHost.Bounds.Height;

        if (hostW <= 0 || hostH <= 0)
        {
            return;
        }

        if (!vm.TryGetVideoPixelSize(out var pxW, out var pxH))
        {
            // Video size not available yet (often until playback starts).
            // IMPORTANT: give VideoView a non-zero size so it creates its native surface.

            // Avoid infinite layout loops by only setting when changed.
            if (_lastIsShowingVideo &&
                _lastVideoSizeUnknown &&
                _lastScaleMode == vm.ScaleMode &&
                _lastAlign == vm.Align &&
                Math.Abs(_lastVideoHostW - hostW) < 0.01 &&
                Math.Abs(_lastVideoHostH - hostH) < 0.01)
            {
                return;
            }

            _lastIsShowingVideo = true;
            _lastVideoSizeUnknown = true;
            _lastScaleMode = vm.ScaleMode;
            _lastAlign = vm.Align;
            _lastVideoHostW = hostW;
            _lastVideoHostH = hostH;
            _lastVideoPxW = 0;
            _lastVideoPxH = 0;

            if (double.IsNaN(_lastVideoW) || Math.Abs(_lastVideoW - hostW) > 0.01)
            {
                ContentVideo.Width = hostW;
                _lastVideoW = hostW;
            }

            if (double.IsNaN(_lastVideoH) || Math.Abs(_lastVideoH - hostH) > 0.01)
            {
                ContentVideo.Height = hostH;
                _lastVideoH = hostH;
            }

            if (double.IsNaN(_lastVideoLeft) || Math.Abs(_lastVideoLeft) > 0.01)
            {
                Canvas.SetLeft(ContentVideo, 0);
                _lastVideoLeft = 0;
            }

            if (double.IsNaN(_lastVideoTop) || Math.Abs(_lastVideoTop) > 0.01)
            {
                Canvas.SetTop(ContentVideo, 0);
                _lastVideoTop = 0;
            }
            return;
        }

        // If nothing relevant changed since the last call, do nothing.
        // This is critical to avoid Avalonia's "Infinite layout loop" detection.
        if (_lastIsShowingVideo &&
            !_lastVideoSizeUnknown &&
            _lastScaleMode == vm.ScaleMode &&
            _lastAlign == vm.Align &&
            _lastVideoPxW == pxW &&
            _lastVideoPxH == pxH &&
            Math.Abs(_lastVideoHostW - hostW) < 0.01 &&
            Math.Abs(_lastVideoHostH - hostH) < 0.01)
        {
            return;
        }

        _lastIsShowingVideo = true;
        _lastVideoSizeUnknown = false;
        _lastScaleMode = vm.ScaleMode;
        _lastAlign = vm.Align;
        _lastVideoHostW = hostW;
        _lastVideoHostH = hostH;
        _lastVideoPxW = pxW;
        _lastVideoPxH = pxH;

        // Match the image behavior:
        // - Fill Height => scale to fill HEIGHT, crop horizontally (Left/Center/Right)
        // - Fill Width  => scale to fill WIDTH, crop vertically (Top/Center/Bottom)
        var scale = vm.ScaleMode == MediaScaleMode.FillHeight
            ? hostH / pxH
            : hostW / pxW;

        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            return;

        var videoW = pxW * scale;
        var videoH = pxH * scale;

        var left = (hostW - videoW) * 0.5;
        var top = (hostH - videoH) * 0.5;

        var a = vm.Align switch
        {
            MediaAlign.Start => 0.0,
            MediaAlign.Center => 0.5,
            MediaAlign.End => 1.0,
            _ => 0.5,
        };

        if (vm.ScaleMode == MediaScaleMode.FillHeight)
        {
            left = (hostW - videoW) * a;
        }
        else
        {
            top = (hostH - videoH) * a;
        }

        if (double.IsNaN(_lastVideoW) || Math.Abs(_lastVideoW - videoW) > 0.01)
        {
            ContentVideo.Width = videoW;
            _lastVideoW = videoW;
        }

        if (double.IsNaN(_lastVideoH) || Math.Abs(_lastVideoH - videoH) > 0.01)
        {
            ContentVideo.Height = videoH;
            _lastVideoH = videoH;
        }

        if (double.IsNaN(_lastVideoLeft) || Math.Abs(_lastVideoLeft - left) > 0.01)
        {
            Canvas.SetLeft(ContentVideo, left);
            _lastVideoLeft = left;
        }

        if (double.IsNaN(_lastVideoTop) || Math.Abs(_lastVideoTop - top) > 0.01)
        {
            Canvas.SetTop(ContentVideo, top);
            _lastVideoTop = top;
        }
    }
}
