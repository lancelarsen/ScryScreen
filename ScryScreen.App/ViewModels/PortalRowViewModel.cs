using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using ScryScreen.App.Services;
using ScryScreen.Core.Utilities;

namespace ScryScreen.App.ViewModels;

public partial class PortalRowViewModel : ViewModelBase, IDisposable
{
    private readonly PortalHostService _portalHost;
    private readonly LibVLC _previewLibVlc;
    private readonly MediaPlayer _previewPlayer;
    private Media? _previewMedia;
    private bool _wasPortalPlaying;
    private readonly VideoPausedFramePrimer _pausedFramePrimer;
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
        _pausedFramePrimer = new VideoPausedFramePrimer(new TaskVideoDelay());
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
        MediaFileClassifier.IsVideo(AssignedMediaFilePath);

    public MediaPlayer PreviewVideoPlayer => _previewPlayer;

    public bool HasVideoTimeline => IsVideoAssigned && _videoLengthMs > 0;

    public string VideoTimeText => TimeFormatter.FormatMs(VideoTimeMs);

    public string VideoDurationText => TimeFormatter.FormatMs(_videoLengthMs);

    [ObservableProperty]
    private long videoTimeMs;

    [ObservableProperty]
    private double videoPosition;

    [ObservableProperty]
    private bool isSelectedForCurrentMedia;

    [ObservableProperty]
    private bool isSelectedForInitiative;

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

    public bool HasMediaPreview => AssignedPreview is not null;

    public bool ShowMonitorSnapshot => AssignedPreview is not null && !IsVideoAssigned;

    public bool ShowMonitorLiveVideo => IsVideoAssigned;

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
        OnPropertyChanged(nameof(ShowMonitorSnapshot));
        OnPropertyChanged(nameof(ShowMonitorLiveVideo));
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

        if (!MediaFileClassifier.IsImage(filePath))
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
        OnPropertyChanged(nameof(ShowMonitorSnapshot));
        OnPropertyChanged(nameof(ShowMonitorLiveVideo));
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

    partial void OnIsVideoPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowMonitorSnapshot));
        OnPropertyChanged(nameof(ShowMonitorLiveVideo));
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

        if (!MonitorPreviewGeometryCalculator.TryCalculate(
                MonitorPreviewOuterW,
                MonitorPreviewOuterH,
                monitorW,
                monitorH,
                mediaW,
                mediaH,
                ScaleMode,
                Align,
                out var geometry))
        {
            PreviewImageLeft = 0;
            PreviewImageTop = 0;
            PreviewImageWidth = 0;
            PreviewImageHeight = 0;
            MonitorPreviewScreenWidth = 0;
            MonitorPreviewScreenHeight = 0;
            return;
        }

        MonitorPreviewScreenWidth = geometry.ScreenWidth;
        MonitorPreviewScreenHeight = geometry.ScreenHeight;
        PreviewImageLeft = geometry.ImageLeft;
        PreviewImageTop = geometry.ImageTop;
        PreviewImageWidth = geometry.ImageWidth;
        PreviewImageHeight = geometry.ImageHeight;
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

        var transitionedToPaused = _wasPortalPlaying && !isPlaying;
        _wasPortalPlaying = isPlaying;

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

        var didSeekPreview = false;

        // Correct drift occasionally.
        try
        {
            var previewTime = _previewPlayer.Time;
            if (Math.Abs(previewTime - timeMs) > 250)
            {
                _previewPlayer.Time = timeMs;
                didSeekPreview = true;
            }
        }
        catch
        {
            // ignore
        }

        // When pausing, LibVLC may not present the newly-seeked paused frame until a decode occurs.
        // Prime a single frame at the current paused time so the preview freezes where we paused.
        if (!isPlaying && (transitionedToPaused || didSeekPreview))
        {
            TryPrimePreviewPausedFrame(timeMs);
        }
    }

    private void TryPrimePreviewPausedFrame(long targetMs)
    {
        if (_pausedFramePrimer.IsPriming)
        {
            return;
        }

        var adapter = new LibVlcMediaPlayerPlaybackAdapter(_previewPlayer);
        _ = Task.Run(async () =>
        {
            try
            {
                await _pausedFramePrimer.PrimePausedFrameAsync(
                    adapter,
                    targetMs,
                    isNativeTargetReady: () => !OperatingSystem.IsWindows() || _previewPlayer.Hwnd != IntPtr.Zero,
                    decodeDelayMs: 90).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        });
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
        IsVideoPlaying = false;
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
