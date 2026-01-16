using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ScryScreen.App.ViewModels;

namespace ScryScreen.App.Views;

public partial class MapMasterView : UserControl
{
    private bool _isErasing;
    private bool _isPointerOverEditor;
    private Rect _portalRect;

    public MapMasterView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) => HookViewModel();
        DetachedFromVisualTree += (_, _) => UnhookViewModel();
        DataContextChanged += (_, _) => HookViewModel();
        EditorSurface.SizeChanged += (_, _) => UpdatePreviewLayout();
    }

    private void HookViewModel()
    {
        UnhookViewModel();

        if (DataContext is MapMasterViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdatePreviewLayout();
    }

    private void UnhookViewModel()
    {
        if (DataContext is MapMasterViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MapMasterViewModel.SourceImage)
            or nameof(MapMasterViewModel.PreviewScaleMode)
            or nameof(MapMasterViewModel.PreviewAlign)
            or nameof(MapMasterViewModel.PreviewPortalAspectRatio))
        {
            Dispatcher.UIThread.Post(UpdatePreviewLayout, DispatcherPriority.Background);
        }
    }

    private void UpdatePreviewLayout()
    {
        if (EditorSurface is null || PreviewImage is null || MaskImage is null)
        {
            return;
        }

        var hostW = EditorSurface.Bounds.Width;
        var hostH = EditorSurface.Bounds.Height;
        if (hostW <= 0 || hostH <= 0)
        {
            return;
        }

        if (DataContext is not MapMasterViewModel vm || vm.SourceImage is null)
        {
            _portalRect = default;

            PreviewImage.Width = double.NaN;
            PreviewImage.Height = double.NaN;
            Canvas.SetLeft(PreviewImage, 0);
            Canvas.SetTop(PreviewImage, 0);

            MaskImage.Width = double.NaN;
            MaskImage.Height = double.NaN;
            Canvas.SetLeft(MaskImage, 0);
            Canvas.SetTop(MaskImage, 0);
            return;
        }

        var ar = vm.PreviewPortalAspectRatio;
        if (ar <= 0 || double.IsNaN(ar) || double.IsInfinity(ar))
        {
            ar = 16.0 / 9.0;
        }

        // Fit the portal-aspect rectangle into the editor surface.
        var hostAr = hostW / hostH;
        double portalW;
        double portalH;
        double portalLeft;
        double portalTop;

        if (hostAr >= ar)
        {
            portalH = hostH;
            portalW = portalH * ar;
            portalLeft = (hostW - portalW) * 0.5;
            portalTop = 0;
        }
        else
        {
            portalW = hostW;
            portalH = portalW / ar;
            portalLeft = 0;
            portalTop = (hostH - portalH) * 0.5;
        }

        _portalRect = new Rect(portalLeft, portalTop, portalW, portalH);

        MaskImage.Width = portalW;
        MaskImage.Height = portalH;
        Canvas.SetLeft(MaskImage, portalLeft);
        Canvas.SetTop(MaskImage, portalTop);

        var pxW = vm.SourceImage.PixelSize.Width;
        var pxH = vm.SourceImage.PixelSize.Height;
        if (pxW <= 0 || pxH <= 0)
        {
            return;
        }

        // Match the portal's FillHeight/FillWidth crop behavior.
        var scale = vm.PreviewScaleMode == MediaScaleMode.FillHeight
            ? portalH / pxH
            : portalW / pxW;

        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return;
        }

        var imageW = pxW * scale;
        var imageH = pxH * scale;

        var left = portalLeft + (portalW - imageW) * 0.5;
        var top = portalTop + (portalH - imageH) * 0.5;

        var a = vm.PreviewAlign switch
        {
            MediaAlign.Start => 0.0,
            MediaAlign.Center => 0.5,
            MediaAlign.End => 1.0,
            _ => 0.5,
        };

        if (vm.PreviewScaleMode == MediaScaleMode.FillHeight)
        {
            left = portalLeft + (portalW - imageW) * a;
        }
        else
        {
            top = portalTop + (portalH - imageH) * a;
        }

        PreviewImage.Width = imageW;
        PreviewImage.Height = imageH;
        Canvas.SetLeft(PreviewImage, left);
        Canvas.SetTop(PreviewImage, top);
    }

    private void OnEditorPointerEntered(object? sender, PointerEventArgs e)
    {
        _isPointerOverEditor = true;
        UpdateEraserCursor(e.GetPosition(EditorSurface));
    }

    private void OnEditorPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerOverEditor = false;
        if (EraserCursor is not null)
        {
            EraserCursor.IsVisible = false;
        }
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MapMasterViewModel)
        {
            return;
        }

        if (!e.GetCurrentPoint(EditorSurface).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isErasing = true;
        e.Pointer.Capture(EditorSurface);
        var p = e.GetPosition(EditorSurface);
        UpdateEraserCursor(p);
        EraseAt(p);
        e.Handled = true;
    }

    private void OnEditorPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not MapMasterViewModel)
        {
            return;
        }

        var p = e.GetPosition(EditorSurface);
        UpdateEraserCursor(p);

        if (_isErasing)
        {
            EraseAt(p);
            e.Handled = true;
        }
    }

    private void OnEditorPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isErasing = false;
        e.Pointer.Capture(null);

        if (DataContext is MapMasterViewModel vm)
        {
            vm.CommitMaskEdits();
        }
    }

    private void EraseAt(Point p)
    {
        if (DataContext is not MapMasterViewModel vm)
        {
            return;
        }

        if (EditorSurface.Bounds.Width <= 0 || EditorSurface.Bounds.Height <= 0)
        {
            return;
        }

        var rect = _portalRect;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            rect = new Rect(0, 0, EditorSurface.Bounds.Width, EditorSurface.Bounds.Height);
        }

        // Only erase inside the portal-aspect area (matches player display).
        if (!rect.Contains(p))
        {
            return;
        }

        vm.EraseAtSurfacePoint(new Point(p.X - rect.X, p.Y - rect.Y), rect.Size);
    }

    private void UpdateEraserCursor(Point p)
    {
        if (EraserCursor is null || EditorSurface is null)
        {
            return;
        }

        if (!_isPointerOverEditor)
        {
            EraserCursor.IsVisible = false;
            return;
        }

        if (DataContext is not MapMasterViewModel vm)
        {
            EraserCursor.IsVisible = false;
            return;
        }

        var rect = _portalRect;
        if (rect.Width > 0 && rect.Height > 0 && !rect.Contains(p))
        {
            EraserCursor.IsVisible = false;
            return;
        }

        var d = vm.EraserDiameter;
        if (d <= 0 || double.IsNaN(d) || double.IsInfinity(d))
        {
            EraserCursor.IsVisible = false;
            return;
        }

        EraserCursor.IsVisible = true;
        Canvas.SetLeft(EraserCursor, p.X - (d * 0.5));
        Canvas.SetTop(EraserCursor, p.Y - (d * 0.5));
    }
}
