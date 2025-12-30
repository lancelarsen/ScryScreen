using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScryScreen.App.Services;

namespace ScryScreen.App.ViewModels;

public partial class PortalRowViewModel : ViewModelBase
{
    private readonly PortalHostService _portalHost;

    public event Action<int>? DeleteRequested;

    public PortalRowViewModel(PortalHostService portalHost, int portalNumber, IReadOnlyList<ScreenInfoViewModel> availableScreens)
    {
        _portalHost = portalHost;
        PortalNumber = portalNumber;
        AvailableScreens = availableScreens;
        IsVisible = true;
        currentAssignment = "Idle";
    }

    public int PortalNumber { get; }

    public string Title => $"Portal {PortalNumber}";

    public IReadOnlyList<ScreenInfoViewModel> AvailableScreens { get; }

    [ObservableProperty]
    private ScreenInfoViewModel? selectedScreen;

    [ObservableProperty]
    private MediaScaleMode scaleMode = MediaScaleMode.FillHeight;

    [ObservableProperty]
    private MediaAlign align = MediaAlign.Center;

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private string currentAssignment;

    [ObservableProperty]
    private string? assignedMediaFilePath;

    [ObservableProperty]
    private bool isSelectedForCurrentMedia;

    [ObservableProperty]
    private Bitmap? assignedPreview;

    [ObservableProperty]
    private double previewImageLeft;

    [ObservableProperty]
    private double previewImageTop;

    [ObservableProperty]
    private double previewImageWidth;

    [ObservableProperty]
    private double previewImageHeight;

    public bool HasMonitorPreview => SelectedScreen is not null;

    public bool HasMediaPreview => AssignedPreview is not null;

    public bool HasMonitorAndMediaPreview => HasMonitorPreview && HasMediaPreview;

    public string FitModeText
    {
        get
        {
            var mode = ScaleMode == MediaScaleMode.FillHeight ? "Fill height" : "Fill width";
            var axisLabel = ScaleMode == MediaScaleMode.FillHeight ? "H" : "W";

            var alignText = Align switch
            {
                MediaAlign.Start => ScaleMode == MediaScaleMode.FillHeight ? "Left" : "Top",
                MediaAlign.Center => "Center",
                MediaAlign.End => ScaleMode == MediaScaleMode.FillHeight ? "Right" : "Bottom",
                _ => Align.ToString(),
            };

            return $"{mode} ({axisLabel}) • {alignText}";
        }
    }

    public bool HasAssignedPreview => AssignedPreview is not null;

    // Once the monitor preview is available, the monitor preview becomes the canonical “what the portal shows” preview.
    // Keep the old top thumbnail only as a fallback when no monitor is selected.
    public bool ShowTopAssignedPreview => HasAssignedPreview && !HasMonitorPreview;

    public string MonitorPreviewToolTip
    {
        get
        {
            if (SelectedScreen is null)
            {
                return string.Empty;
            }

            // Put the most important info first and keep it compact for a hover tooltip.
            return $"{SelectedScreen.ResolutionText}\n{SelectedScreen.AspectRatioText}\n{FitModeText}";
        }
    }

    partial void OnAssignedPreviewChanged(Bitmap? oldValue, Bitmap? newValue)
    {
        oldValue?.Dispose();
        OnPropertyChanged(nameof(HasAssignedPreview));
        OnPropertyChanged(nameof(ShowTopAssignedPreview));
        OnPropertyChanged(nameof(HasMediaPreview));
        OnPropertyChanged(nameof(HasMonitorAndMediaPreview));
        OnPropertyChanged(nameof(MonitorPreviewToolTip));
        UpdateMonitorPreviewGeometry();
    }

    public void SetAssignedPreviewFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            AssignedPreview = null;
            return;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            AssignedPreview = new Bitmap(stream);
        }
        catch
        {
            AssignedPreview = null;
        }
    }

    partial void OnSelectedScreenChanged(ScreenInfoViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        _portalHost.AssignToScreen(PortalNumber, value);
        OnPropertyChanged(nameof(HasMonitorPreview));
        OnPropertyChanged(nameof(HasMonitorAndMediaPreview));
        OnPropertyChanged(nameof(ShowTopAssignedPreview));
        OnPropertyChanged(nameof(MonitorPreviewToolTip));
        UpdateMonitorPreviewGeometry();
    }

    partial void OnScaleModeChanged(MediaScaleMode value)
    {
        OnPropertyChanged(nameof(FitModeText));
        OnPropertyChanged(nameof(MonitorPreviewToolTip));
        UpdateMonitorPreviewGeometry();
    }

    partial void OnAlignChanged(MediaAlign value)
    {
        OnPropertyChanged(nameof(FitModeText));
        OnPropertyChanged(nameof(MonitorPreviewToolTip));
        UpdateMonitorPreviewGeometry();
    }

    private void UpdateMonitorPreviewGeometry()
    {
        if (SelectedScreen is null || AssignedPreview is null)
        {
            PreviewImageLeft = 0;
            PreviewImageTop = 0;
            PreviewImageWidth = 0;
            PreviewImageHeight = 0;
            return;
        }

        var monitorW = Math.Max(1, SelectedScreen.WidthPx);
        var monitorH = Math.Max(1, SelectedScreen.HeightPx);

        var mediaW = Math.Max(1, AssignedPreview.PixelSize.Width);
        var mediaH = Math.Max(1, AssignedPreview.PixelSize.Height);

        var sx = monitorW / (double)mediaW;
        var sy = monitorH / (double)mediaH;

        var scale = ScaleMode switch
        {
            // Match PortalWindow behavior (see PortalWindow.axaml.cs):
            // FillHeight (H) => fill height (sy)
            // FillWidth  (W) => fill width (sx)
            MediaScaleMode.FillHeight => sy,
            MediaScaleMode.FillWidth => sx,
            _ => sx,
        };

        var displayW = mediaW * scale;
        var displayH = mediaH * scale;

        PreviewImageWidth = displayW;
        PreviewImageHeight = displayH;

        var leftoverX = monitorW - displayW;
        var leftoverY = monitorH - displayH;

        // Align only on the axis that might overflow/letterbox for the selected scale mode.
        // FillHeight (H) => horizontal align; FillWidth (W) => vertical align.
        var ax = Align switch
        {
            MediaAlign.Start => 0.0,
            MediaAlign.Center => 0.5,
            MediaAlign.End => 1.0,
            _ => 0.5,
        };

        PreviewImageLeft = ScaleMode == MediaScaleMode.FillHeight ? leftoverX * ax : leftoverX * 0.5;
        PreviewImageTop = ScaleMode == MediaScaleMode.FillWidth ? leftoverY * ax : leftoverY * 0.5;
    }

    partial void OnIsVisibleChanged(bool value)
    {
        _portalHost.SetVisibility(PortalNumber, value);
    }

    [RelayCommand]
    private void DeletePortal()
    {
        DeleteRequested?.Invoke(PortalNumber);
    }

    [RelayCommand]
    private void ClosePortal()
    {
        // Close = remove this portal window + row.
        DeleteRequested?.Invoke(PortalNumber);
    }
}
