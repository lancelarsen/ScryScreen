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
        if (e.PropertyName is nameof(MapMasterViewModel.SourceImage) or nameof(MapMasterViewModel.PreviewScaleMode) or nameof(MapMasterViewModel.PreviewAlign))
        {
            Dispatcher.UIThread.Post(UpdatePreviewLayout, DispatcherPriority.Background);
        }
    }

    private void UpdatePreviewLayout()
    {
        if (PreviewImage is null || EditorSurface is null || PreviewCanvas is null)
        {
            return;
        }

        if (DataContext is not MapMasterViewModel vm || vm.SourceImage is null)
        {
            PreviewImage.Width = double.NaN;
            PreviewImage.Height = double.NaN;
            Canvas.SetLeft(PreviewImage, 0);
            Canvas.SetTop(PreviewImage, 0);
            return;
        }

        var hostW = EditorSurface.Bounds.Width;
        var hostH = EditorSurface.Bounds.Height;
        if (hostW <= 0 || hostH <= 0)
        {
            return;
        }

        var pxW = vm.SourceImage.PixelSize.Width;
        var pxH = vm.SourceImage.PixelSize.Height;
        if (pxW <= 0 || pxH <= 0)
        {
            return;
        }

        // Match the portal's FillHeight/FillWidth crop behavior.
        var scale = vm.PreviewScaleMode == MediaScaleMode.FillHeight
            ? hostH / pxH
            : hostW / pxW;

        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return;
        }

        var imageW = pxW * scale;
        var imageH = pxH * scale;

        var left = (hostW - imageW) * 0.5;
        var top = (hostH - imageH) * 0.5;

        var a = vm.PreviewAlign switch
        {
            MediaAlign.Start => 0.0,
            MediaAlign.Center => 0.5,
            MediaAlign.End => 1.0,
            _ => 0.5,
        };

        if (vm.PreviewScaleMode == MediaScaleMode.FillHeight)
        {
            left = (hostW - imageW) * a;
        }
        else
        {
            top = (hostH - imageH) * a;
        }

        PreviewImage.Width = imageW;
        PreviewImage.Height = imageH;
        Canvas.SetLeft(PreviewImage, left);
        Canvas.SetTop(PreviewImage, top);
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

        vm.EraseAtSurfacePoint(p, EditorSurface.Bounds.Size);
    }
}
