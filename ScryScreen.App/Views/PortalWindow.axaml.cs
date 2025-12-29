using System;
using System.ComponentModel;
using Avalonia.Controls;
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

    public PortalWindow()
    {
        InitializeComponent();

        // Keep image layout in sync with fullscreen transitions, DPI scaling, etc.
        SizeChanged += (_, _) => UpdateImageLayout();
        ImageHost.SizeChanged += (_, _) => UpdateImageLayout();
        DataContextChanged += (_, _) => HookViewModel();
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
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PortalWindowViewModel.ContentImage) or nameof(PortalWindowViewModel.ScaleMode) or nameof(PortalWindowViewModel.Align) or nameof(PortalWindowViewModel.IsShowingImage))
        {
            UpdateImageLayout();
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
            // Only change properties if needed; avoids layout invalidations during steady-state.
            if (!double.IsNaN(ContentImage.Width)) ContentImage.Width = double.NaN;
            if (!double.IsNaN(ContentImage.Height)) ContentImage.Height = double.NaN;
            if (ContentImage.HorizontalAlignment != Avalonia.Layout.HorizontalAlignment.Center)
                ContentImage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            if (ContentImage.VerticalAlignment != Avalonia.Layout.VerticalAlignment.Center)
                ContentImage.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;

            _lastHostW = double.NaN;
            _lastHostH = double.NaN;
            _lastIsShowingImage = false;
            _lastImageRef = null;
            return;
        }

        var hostW = ImageHost.Bounds.Width;
        var hostH = ImageHost.Bounds.Height;

        if (hostW <= 0 || hostH <= 0)
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
        // - Fill Height => scale to fill WIDTH, crop vertically (Top/Center/Bottom)
        // - Fill Width  => scale to fill HEIGHT, crop horizontally (Left/Center/Right)
        if (!double.IsNaN(ContentImage.Width)) ContentImage.Width = double.NaN;
        if (!double.IsNaN(ContentImage.Height)) ContentImage.Height = double.NaN;

        if (vm.ScaleMode == MediaScaleMode.FillHeight)
        {
            if (Math.Abs(ContentImage.Width - hostW) > 0.01) ContentImage.Width = hostW;
            if (ContentImage.HorizontalAlignment != Avalonia.Layout.HorizontalAlignment.Center)
                ContentImage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;

            var vertical = vm.Align switch
            {
                MediaAlign.Start => Avalonia.Layout.VerticalAlignment.Top,
                MediaAlign.Center => Avalonia.Layout.VerticalAlignment.Center,
                MediaAlign.End => Avalonia.Layout.VerticalAlignment.Bottom,
                _ => Avalonia.Layout.VerticalAlignment.Center,
            };

            if (ContentImage.VerticalAlignment != vertical)
                ContentImage.VerticalAlignment = vertical;
        }
        else
        {
            if (Math.Abs(ContentImage.Height - hostH) > 0.01) ContentImage.Height = hostH;
            if (ContentImage.VerticalAlignment != Avalonia.Layout.VerticalAlignment.Center)
                ContentImage.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;

            var horizontal = vm.Align switch
            {
                MediaAlign.Start => Avalonia.Layout.HorizontalAlignment.Left,
                MediaAlign.Center => Avalonia.Layout.HorizontalAlignment.Center,
                MediaAlign.End => Avalonia.Layout.HorizontalAlignment.Right,
                _ => Avalonia.Layout.HorizontalAlignment.Center,
            };

            if (ContentImage.HorizontalAlignment != horizontal)
                ContentImage.HorizontalAlignment = horizontal;
        }
    }
}
