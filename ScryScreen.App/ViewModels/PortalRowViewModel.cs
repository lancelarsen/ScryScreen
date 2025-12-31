using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using ScryScreen.App.Services;

namespace ScryScreen.App.ViewModels;

public partial class PortalRowViewModel : ViewModelBase, IDisposable
{
    private readonly PortalHostService _portalHost;
    private readonly LibVLC _previewLibVlc;
    private readonly MediaPlayer _previewPlayer;
    private Media? _previewMedia;
    private readonly DispatcherTimer _videoSyncTimer;

    private long _videoLengthMs;
    private int _videoPixelW;
    private int _videoPixelH;
    private bool _isUpdatingFromPlayer;

    private const double MonitorPreviewOuterW = 120;
    private const double MonitorPreviewOuterH = 60;

    public event Action<int>? DeleteRequested;

    public PortalRowViewModel(PortalHostService portalHost, int portalNumber, IReadOnlyList<ScreenInfoViewModel> availableScreens)
    {
        _portalHost = portalHost;
        PortalNumber = portalNumber;
        AvailableScreens = availableScreens;
        IsVisible = true;
        currentAssignment = "Idle";

        _previewLibVlc = new LibVLC("--no-video-title-show");
        _previewPlayer = new MediaPlayer(_previewLibVlc);
        _videoSyncTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => SyncVideo());
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
    private bool isVideoPlaying;

    [ObservableProperty]
    private bool isVideoLoop;

    public bool IsVideoAssigned =>
        !string.IsNullOrWhiteSpace(AssignedMediaFilePath) &&
        string.Equals(Path.GetExtension(AssignedMediaFilePath), ".mp4", StringComparison.OrdinalIgnoreCase);

    public MediaPlayer PreviewVideoPlayer => _previewPlayer;

    public bool HasVideoTimeline => IsVideoAssigned && _videoLengthMs > 0;

    public string VideoTimeText => FormatTime(VideoTimeMs);

    public string VideoDurationText => FormatTime(_videoLengthMs);

    [ObservableProperty]
    private long videoTimeMs;

    [ObservableProperty]
    private double videoPosition;

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

    [ObservableProperty]
    private double monitorPreviewScreenWidth;

    [ObservableProperty]
    private double monitorPreviewScreenHeight;

    public bool HasMonitorPreview => SelectedScreen is not null;

    public bool HasMediaPreview => AssignedPreview is not null && !IsVideoAssigned;

    public bool HasMonitorAndMediaPreview => HasMonitorPreview && HasMediaPreview;

    public string FitModeText
    {
        get
        {
            var mode = ScaleMode == MediaScaleMode.FillHeight ? "Fill Height" : "Fill Width";

            var alignText = Align switch
            {
                MediaAlign.Start => ScaleMode == MediaScaleMode.FillHeight ? "Left" : "Top",
                MediaAlign.Center => "Center",
                MediaAlign.End => ScaleMode == MediaScaleMode.FillHeight ? "Right" : "Bottom",
                _ => Align.ToString(),
            };

            return $"{mode} • {alignText}";
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

        var ext = Path.GetExtension(filePath);
        var isImage = ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                      ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                      ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                      ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                      ext.Equals(".gif", StringComparison.OrdinalIgnoreCase);

        if (!isImage)
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

    partial void OnAssignedMediaFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(IsVideoAssigned));
        OnPropertyChanged(nameof(HasMediaPreview));
        OnPropertyChanged(nameof(HasMonitorAndMediaPreview));
        OnPropertyChanged(nameof(MonitorPreviewToolTip));

        if (IsVideoAssigned)
        {
            EnsurePreviewMedia();
            _videoSyncTimer.Start();
        }
        else
        {
            _videoSyncTimer.Stop();
            ClearPreviewMedia();

            _videoLengthMs = 0;
            _videoPixelW = 0;
            _videoPixelH = 0;

            _isUpdatingFromPlayer = true;
            try
            {
                VideoTimeMs = 0;
                VideoPosition = 0;
            }
            finally
            {
                _isUpdatingFromPlayer = false;
            }

            IsVideoPlaying = false;
            IsVideoLoop = false;
        }

        OnPropertyChanged(nameof(HasVideoTimeline));
        OnPropertyChanged(nameof(VideoDurationText));
        OnPropertyChanged(nameof(VideoTimeText));
        UpdateMonitorPreviewGeometry();
    }

    partial void OnVideoTimeMsChanged(long value)
    {
        OnPropertyChanged(nameof(VideoTimeText));
    }

    partial void OnVideoPositionChanged(double value)
    {
        if (_isUpdatingFromPlayer)
        {
            return;
        }

        if (!IsVideoAssigned || _videoLengthMs <= 0)
        {
            return;
        }

        var clamped = Math.Clamp(value, 0, 1);
        var target = (long)Math.Round(_videoLengthMs * clamped);
        _portalHost.SeekVideo(PortalNumber, target);

        if (!IsVideoAssigned)
        {
            IsVideoPlaying = false;
            IsVideoLoop = false;
        }
    }

    partial void OnIsVideoLoopChanged(bool value)
    {
        if (!IsVideoAssigned)
        {
            return;
        }

        _portalHost.SetVideoLoop(PortalNumber, value);
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
        if (SelectedScreen is null)
        {
            PreviewImageLeft = 0;
            PreviewImageTop = 0;
            PreviewImageWidth = 0;
            PreviewImageHeight = 0;
            MonitorPreviewScreenWidth = 0;
            MonitorPreviewScreenHeight = 0;
            return;
        }

        var mediaW = 0;
        var mediaH = 0;

        if (AssignedPreview is not null)
        {
            mediaW = AssignedPreview.PixelSize.Width;
            mediaH = AssignedPreview.PixelSize.Height;
        }
        else if (IsVideoAssigned && _videoPixelW > 0 && _videoPixelH > 0)
        {
            mediaW = _videoPixelW;
            mediaH = _videoPixelH;
        }
        else
        {
            PreviewImageLeft = 0;
            PreviewImageTop = 0;
            PreviewImageWidth = 0;
            PreviewImageHeight = 0;
            MonitorPreviewScreenWidth = 0;
            MonitorPreviewScreenHeight = 0;
            return;
        }

        var monitorW = Math.Max(1, SelectedScreen.WidthPx);
        var monitorH = Math.Max(1, SelectedScreen.HeightPx);

        // Scale the actual monitor aspect ratio to fit the fixed preview box (120x60)
        var monitorScale = Math.Min(MonitorPreviewOuterW / monitorW, MonitorPreviewOuterH / monitorH);
        var screenW = monitorW * monitorScale;
        var screenH = monitorH * monitorScale;
        MonitorPreviewScreenWidth = screenW;
        MonitorPreviewScreenHeight = screenH;

        mediaW = Math.Max(1, mediaW);
        mediaH = Math.Max(1, mediaH);

        var sx = screenW / mediaW;
        var sy = screenH / mediaH;

        // Stabilize the filled axis to avoid sub-pixel drift that can show as a 1px gap.
        // FillHeight (H) => displayed height matches monitorH exactly.
        // FillWidth  (W) => displayed width  matches monitorW exactly.
        var displayW = 0.0;
        var displayH = 0.0;

        switch (ScaleMode)
        {
            case MediaScaleMode.FillHeight:
                displayH = screenH;
                displayW = mediaW * sy;
                break;
            case MediaScaleMode.FillWidth:
                displayW = screenW;
                displayH = mediaH * sx;
                break;
            default:
                displayW = screenW;
                displayH = mediaH * sx;
                break;
        }

        PreviewImageWidth = displayW;
        PreviewImageHeight = displayH;

        var leftoverX = screenW - displayW;
        var leftoverY = screenH - displayH;

        // Clamp tiny floating-point leftovers to 0 to prevent hairline slivers.
        if (Math.Abs(leftoverX) < 1e-6)
        {
            leftoverX = 0;
        }

        if (Math.Abs(leftoverY) < 1e-6)
        {
            leftoverY = 0;
        }

        // Align only on the axis that might overflow/letterbox for the selected scale mode.
        // FillHeight (H) => horizontal align; FillWidth (W) => vertical align.
        var ax = Align switch
        {
            MediaAlign.Start => 0.0,
            MediaAlign.Center => 0.5,
            MediaAlign.End => 1.0,
            _ => 0.5,
        };

        var left = ScaleMode == MediaScaleMode.FillHeight ? leftoverX * ax : leftoverX * 0.5;
        var top = ScaleMode == MediaScaleMode.FillWidth ? leftoverY * ax : leftoverY * 0.5;

        // Snap to pixel-ish boundaries for the preview to avoid hairline gaps caused by sub-pixel rendering.
        // Oversize slightly (ceil) and shift slightly (floor) is safe because the host clips.
        PreviewImageWidth = Math.Ceiling(PreviewImageWidth);
        PreviewImageHeight = Math.Ceiling(PreviewImageHeight);
        PreviewImageLeft = Math.Floor(left);
        PreviewImageTop = Math.Floor(top);
    }

    private void EnsurePreviewMedia()
    {
        if (!IsVideoAssigned || string.IsNullOrWhiteSpace(AssignedMediaFilePath))
        {
            return;
        }

        try
        {
            _previewMedia?.Dispose();
            _previewMedia = new Media(_previewLibVlc, new Uri(AssignedMediaFilePath));
            _previewPlayer.Media = _previewMedia;
            _previewPlayer.Pause();
        }
        catch
        {
            // ignore
        }
    }

    private void ClearPreviewMedia()
    {
        try
        {
            _previewPlayer.Stop();
            _previewPlayer.Media = null;
        }
        catch
        {
            // ignore
        }

        try
        {
            _previewMedia?.Dispose();
        }
        catch
        {
            // ignore
        }
        _previewMedia = null;
    }

    private void SyncVideo()
    {
        if (!IsVideoAssigned)
        {
            return;
        }

        var (timeMs, lengthMs, isPlaying) = _portalHost.GetVideoState(PortalNumber);
        if (lengthMs > 0)
        {
            _videoLengthMs = lengthMs;
        }

        var (pxW, pxH) = _portalHost.GetVideoPixelSize(PortalNumber);
        if (pxW > 0 && pxH > 0 && (_videoPixelW != pxW || _videoPixelH != pxH))
        {
            _videoPixelW = pxW;
            _videoPixelH = pxH;
            UpdateMonitorPreviewGeometry();
        }

        _isUpdatingFromPlayer = true;
        try
        {
            IsVideoPlaying = isPlaying;
            VideoTimeMs = Math.Max(0, timeMs);

            if (_videoLengthMs > 0)
            {
                VideoPosition = Math.Clamp(VideoTimeMs / (double)_videoLengthMs, 0, 1);
            }
            else
            {
                VideoPosition = 0;
            }
        }
        finally
        {
            _isUpdatingFromPlayer = false;
        }

        OnPropertyChanged(nameof(HasVideoTimeline));
        OnPropertyChanged(nameof(VideoDurationText));

        // Mirror play/pause
        try
        {
            if (isPlaying)
            {
                if (!_previewPlayer.IsPlaying)
                {
                    _previewPlayer.Play();
                }
            }
            else
            {
                if (_previewPlayer.IsPlaying)
                {
                    _previewPlayer.Pause();
                }
            }
        }
        catch
        {
            // ignore
        }

        // Correct drift occasionally.
        try
        {
            var previewTime = _previewPlayer.Time;
            if (Math.Abs(previewTime - timeMs) > 250)
            {
                _previewPlayer.Time = timeMs;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string FormatTime(long ms)
    {
        if (ms < 0) ms = 0;
        var ts = TimeSpan.FromMilliseconds(ms);
        if (ts.TotalHours >= 1)
        {
            return ts.ToString("h\\:mm\\:ss");
        }
        return ts.ToString("m\\:ss");
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

    [RelayCommand]
    private void ToggleVideoPlayPause()
    {
        if (!IsVideoAssigned)
        {
            return;
        }

        IsVideoPlaying = _portalHost.ToggleVideoPlayPause(PortalNumber);
    }

    [RelayCommand]
    private void RestartVideo()
    {
        if (!IsVideoAssigned)
        {
            return;
        }

        _portalHost.RestartVideo(PortalNumber);
        IsVideoPlaying = true;
    }

    public void Dispose()
    {
        _videoSyncTimer.Stop();
        ClearPreviewMedia();

        try
        {
            _previewPlayer.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            _previewLibVlc.Dispose();
        }
        catch
        {
            // ignore
        }
    }
}
