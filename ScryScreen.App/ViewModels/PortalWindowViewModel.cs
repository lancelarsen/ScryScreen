using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LibVLCSharp.Shared;
using ScryScreen.App.Models;
using ScryScreen.App.Services;
using ScryScreen.Core.InitiativeTracker;

namespace ScryScreen.App.ViewModels;

public partial class PortalWindowViewModel : ViewModelBase, IDisposable
{
    private Bitmap? _contentImage;
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly Timer _loopTimer;
    private readonly VideoLoopController _loopController;
    private readonly VideoPausedFramePrimer _pausedFramePrimer;
    private readonly VideoLoopRestarter<Media> _loopRestarter;
    private readonly IVideoLoopRestartTarget<Media> _loopRestartTarget;
    private Media? _currentVideoMedia;
    private string? _contentVideoPath;
    private bool _loopVideo;
    private long? _pendingSeekTimeMs;
    private bool _pendingPrimeFrame;
    private InitiativePortalViewModel? _initiative;

    public PortalWindowViewModel(int portalNumber)
    {
        PortalNumber = portalNumber;
        ScreenName = "Unassigned";
        ContentTitle = "Idle";
        IsContentVisible = true;
        IsSetup = true;
        IsIdentifyOverlayVisible = false;
        ScaleMode = MediaScaleMode.FillHeight;
        Align = MediaAlign.Center;

        _libVlc = new LibVLC("--no-video-title-show");
        _mediaPlayer = new MediaPlayer(_libVlc);

        _pausedFramePrimer = new VideoPausedFramePrimer(new TaskVideoDelay());

        _loopRestartTarget = new LibVlcLoopRestartTarget(
            _mediaPlayer,
            isNativeTargetReady: () => !OperatingSystem.IsWindows() || _mediaPlayer.Hwnd != IntPtr.Zero);

        _loopRestarter = new VideoLoopRestarter<Media>(
            target: _loopRestartTarget,
            mediaFactory: new LibVlcMediaFactory(_libVlc),
            sleeper: new ThreadVideoSleeper());

        _loopController = new VideoLoopController(
            restart: TryRestartLoopPlayback,
            utcNowTicks: () => DateTime.UtcNow.Ticks,
            isNativeTargetReady: () => _loopRestartTarget.IsNativeTargetReady);

        // EndReached is raised on a LibVLC thread; do not do heavy/re-entrant operations here.
        // Just signal the loop timer to restart safely.
        _mediaPlayer.EndReached += (_, _) =>
        {
            _loopController.SignalEndReached();
        };

        // Timer-based loop restart to avoid calling Stop/Play inside EndReached.
        // The timer does nothing unless a restart is requested.
        _loopTimer = new Timer(_ => LoopTick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void UpdateLoopTimer()
    {
        try
        {
            // Run whenever looping is enabled and a video is loaded.
            // We intentionally do not gate this on IsPlaying: LibVLC can transition to stopped-at-end,
            // and we still need the timer alive to perform the restart.
            var shouldRun = _loopVideo && HasVideo;
            _loopTimer.Change(
                shouldRun ? TimeSpan.FromMilliseconds(100) : Timeout.InfiniteTimeSpan,
                shouldRun ? TimeSpan.FromMilliseconds(100) : Timeout.InfiniteTimeSpan);
        }
        catch
        {
            // ignore
        }
    }

    private void LoopTick()
    {
        if (!_loopVideo || !HasVideo)
        {
            return;
        }

        _loopController.Tick();
    }

    private bool TryRestartLoopPlayback()
    {
        // Designed to be called from the loop timer callback.
        // Keep the policy in a testable strategy.
        return _loopRestarter.TryRestart(ref _currentVideoMedia, _contentVideoPath);
    }

    public int PortalNumber { get; }

    [ObservableProperty]
    private string screenName = string.Empty;

    [ObservableProperty]
    private bool isContentVisible;

    [ObservableProperty]
    private string contentTitle = string.Empty;

    public Bitmap? ContentImage
    {
        get => _contentImage;
        private set
        {
            if (ReferenceEquals(_contentImage, value))
            {
                return;
            }

            _contentImage?.Dispose();
            _contentImage = value;
            OnPropertyChanged(nameof(ContentImage));
            OnPropertyChanged(nameof(HasImage));
            OnPropertyChanged(nameof(IsShowingImage));
            OnPropertyChanged(nameof(IsShowingText));
            OnPropertyChanged(nameof(IsShowingVideo));
            OnPropertyChanged(nameof(IsShowingIdleLogo));
            OnPropertyChanged(nameof(IsShowingNonIdleText));
        }
    }

    public bool HasImage => ContentImage is not null;

    public bool HasVideo => !string.IsNullOrWhiteSpace(_contentVideoPath);

    public bool HasInitiative => _initiative is not null;

    public InitiativePortalViewModel? Initiative
    {
        get => _initiative;
        private set
        {
            if (ReferenceEquals(_initiative, value))
            {
                return;
            }

            _initiative = value;
            OnPropertyChanged(nameof(Initiative));
            OnPropertyChanged(nameof(HasInitiative));
            OnPropertyChanged(nameof(IsShowingInitiative));
            OnPropertyChanged(nameof(IsShowingText));
        }
    }

    public string? ContentVideoPath => _contentVideoPath;

    public MediaPlayer VideoPlayer => _mediaPlayer;

    public (long TimeMs, long LengthMs, bool IsPlaying) GetVideoState()
    {
        if (!HasVideo)
        {
            return (0, 0, false);
        }

        try
        {
            return (_mediaPlayer.Time, _mediaPlayer.Length, _mediaPlayer.IsPlaying);
        }
        catch
        {
            return (0, 0, false);
        }
    }

    public void SeekVideo(long timeMs)
    {
        if (!HasVideo)
        {
            return;
        }

        _pendingSeekTimeMs = timeMs;
        _pendingPrimeFrame = true;

        try
        {
            if (timeMs < 0) timeMs = 0;
            var length = _mediaPlayer.Length;
            if (length > 0 && timeMs > length) timeMs = length;
            _mediaPlayer.Time = timeMs;
        }
        catch
        {
            // ignore
        }

        // If the native rendering target is already attached, prime one frame so
        // LibVLC actually applies the seek while paused (otherwise Time can remain 0
        // until the first decode happens).
        try
        {
            if (_loopRestartTarget.IsNativeTargetReady)
            {
                _ = PrimePausedFrameIfNeededAsync();
            }
        }
        catch
        {
            // ignore
        }
    }

    public bool IsShowingImage => !IsSetup && HasImage;

    public bool IsShowingVideo => !IsSetup && HasVideo;

    public bool IsShowingInitiative => !IsSetup && HasInitiative;

    public bool IsShowingText => !IsSetup && !HasInitiative && !HasImage && !HasVideo;

    public bool IsShowingIdleLogo =>
        IsShowingText && string.Equals(ContentTitle, "Idle", StringComparison.OrdinalIgnoreCase);

    public bool IsShowingNonIdleText => IsShowingText && !IsShowingIdleLogo;

    [ObservableProperty]
    private bool isSetup;

    partial void OnIsSetupChanged(bool value)
    {
        OnPropertyChanged(nameof(IsShowingImage));
        OnPropertyChanged(nameof(IsShowingVideo));
        OnPropertyChanged(nameof(IsShowingInitiative));
        OnPropertyChanged(nameof(IsShowingText));
        OnPropertyChanged(nameof(IsShowingIdleLogo));
        OnPropertyChanged(nameof(IsShowingNonIdleText));
    }

    partial void OnContentTitleChanged(string value)
    {
        OnPropertyChanged(nameof(IsShowingIdleLogo));
        OnPropertyChanged(nameof(IsShowingNonIdleText));
    }

    [ObservableProperty]
    private bool isIdentifyOverlayVisible;

    [ObservableProperty]
    private MediaScaleMode scaleMode;

    [ObservableProperty]
    private MediaAlign align;

    [ObservableProperty]
    private OverlayEffectsState overlayEffects = OverlayEffectsState.None;

    public void SetOverlayEffects(OverlayEffectsState state)
    {
        OverlayEffects = state ?? OverlayEffectsState.None;
    }

    public void ShowIdentifyOverlay() => IsIdentifyOverlayVisible = true;

    public void HideIdentifyOverlay() => IsIdentifyOverlayVisible = false;

    public void SetImage(Bitmap bitmap, string title)
    {
        ContentTitle = title;
        Initiative = null;
        ClearVideoInternal();
        ContentImage = bitmap;
    }

    public void SetVideo(string filePath, string title, bool loop)
    {
        ContentTitle = title;
        Initiative = null;
        ContentImage = null;
        _contentVideoPath = filePath;
        _loopVideo = loop;
        _loopController.SetHasVideo(true);
        _loopController.SetEnabled(loop);
        _loopController.SetArmed(false);
        _loopController.ResetPending();
        // Videos always start paused by default.
        _pendingSeekTimeMs = null;
        _pendingPrimeFrame = false;

        try
        {
            _currentVideoMedia?.Dispose();
            _currentVideoMedia = new Media(_libVlc, new Uri(filePath));
            _mediaPlayer.Media = _currentVideoMedia;
        }
        catch
        {
            // If media cannot be loaded, fall back to text.
            _contentVideoPath = null;
            _pendingSeekTimeMs = null;
            _pendingPrimeFrame = false;
        }

        OnPropertyChanged(nameof(ContentVideoPath));
        OnPropertyChanged(nameof(HasVideo));
        OnPropertyChanged(nameof(IsShowingVideo));
        OnPropertyChanged(nameof(IsShowingText));
        OnPropertyChanged(nameof(IsShowingIdleLogo));
        OnPropertyChanged(nameof(IsShowingNonIdleText));

        UpdateLoopTimer();

        // NOTE: Don't call Play() here.
        // If the VideoView isn't attached/visible yet, LibVLC will fall back to spawning its own window.
    }

    public void PrimePausedFrameIfNeeded()
    {
        _ = PrimePausedFrameIfNeededAsync();
    }

    private async Task PrimePausedFrameIfNeededAsync()
    {
        if (!HasVideo || _mediaPlayer.IsPlaying)
        {
            return;
        }

        if (!_pendingPrimeFrame || !_pendingSeekTimeMs.HasValue)
        {
            return;
        }

        var target = _pendingSeekTimeMs.Value;
        var adapter = new LibVlcMediaPlayerPlaybackAdapter(_mediaPlayer);
        var primed = false;

        try
        {
            primed = await _pausedFramePrimer.PrimePausedFrameAsync(
                adapter,
                target,
                isNativeTargetReady: () => _loopRestartTarget.IsNativeTargetReady,
                decodeDelayMs: 120).ConfigureAwait(false);
        }
        catch
        {
            primed = false;
        }

        if (primed)
        {
            _pendingPrimeFrame = false;
        }
    }

    public void SetText(string text)
    {
        ContentTitle = text;
        Initiative = null;
        ContentImage = null;
        ClearVideoInternal();
        IsSetup = false;
    }

    public void SetInitiative(InitiativeTrackerState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        ContentTitle = "Initiative";
        ContentImage = null;
        ClearVideoInternal();

        if (Initiative is null)
        {
            Initiative = new InitiativePortalViewModel(state);
        }
        else
        {
            Initiative.Update(state);
        }

        IsSetup = false;
    }

    public void SetInitiativeOverlay(InitiativeTrackerState state, double overlayOpacity, InitiativePortalFontSize fontSize)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        // Overlay mode: do NOT clear existing image/video content.
        if (Initiative is null)
        {
            Initiative = new InitiativePortalViewModel(state);
        }
        else
        {
            Initiative.Update(state);
        }

        Initiative.OverlayOpacity = overlayOpacity;
        Initiative.PortalFontSize = fontSize;

        IsSetup = false;
    }

    public void SetVideoLoop(bool loop)
    {
        _loopVideo = loop;
        _loopController.SetEnabled(loop);
        if (!loop)
        {
            _loopController.ResetPending();
        }
        UpdateLoopTimer();
    }

    public bool ToggleVideoPlayPause()
    {
        if (!HasVideo)
        {
            return false;
        }


        try
        {
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
                _loopController.SetArmed(false);

                // Ensure we freeze on the current frame (some codecs/vout paths don't update
                // the displayed frame at pause time unless a decode occurs).
                try
                {
                    _pendingSeekTimeMs = _mediaPlayer.Time;
                    _pendingPrimeFrame = true;
                }
                catch
                {
                    // ignore
                }

                try
                {
                    if (_loopRestartTarget.IsNativeTargetReady)
                    {
                        _ = PrimePausedFrameIfNeededAsync();
                    }
                }
                catch
                {
                    // ignore
                }

                UpdateLoopTimer();
                return false;
            }

            if (_pendingSeekTimeMs.HasValue)
            {
                try
                {
                    _mediaPlayer.Time = _pendingSeekTimeMs.Value;
                }
                catch
                {
                    // ignore
                }
                finally
                {
                    _pendingSeekTimeMs = null;
                    _pendingPrimeFrame = false;
                }
            }

            _mediaPlayer.Play();
            _loopController.SetArmed(true);
            UpdateLoopTimer();
            return true;
        }
        catch
        {
            return _mediaPlayer.IsPlaying;
        }
    }

    public void RestartVideo()
    {
        if (!HasVideo)
        {
            return;
        }


        // Restart = go to the beginning and remain paused.
        // Prime a frame so the first frame displays reliably.
        _pendingSeekTimeMs = 0;
        _pendingPrimeFrame = true;
        _loopController.SetArmed(false);
        _loopController.ResetPending();

        try
        {
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
            }

            _mediaPlayer.Time = 0;
        }
        catch
        {
            // ignore
        }

        UpdateLoopTimer();

        try
        {
            _ = PrimePausedFrameIfNeededAsync();
        }
        catch
        {
            // ignore
        }
    }

    public async Task<Bitmap?> CaptureVideoPreviewAsync(int maxWidth = 640, int maxHeight = 360)
    {
        if (!HasVideo)
        {
            return null;
        }

        // Snapshot works most reliably after at least one frame is decoded.
        var tempPath = Path.Combine(Path.GetTempPath(), $"scry_portal_{PortalNumber}_video_preview_{Guid.NewGuid():N}.png");

        var wasPlaying = false;
        try
        {
            wasPlaying = _mediaPlayer.IsPlaying;

            // Decode a frame from the start, then pause.
            try
            {
                _mediaPlayer.Time = 0;
            }
            catch
            {
                // ignore
            }

            try
            {
                _mediaPlayer.Play();
            }
            catch
            {
                // ignore
            }

            await Task.Delay(150).ConfigureAwait(false);

            try
            {
                _mediaPlayer.TakeSnapshot(0, tempPath, (uint)maxWidth, (uint)maxHeight);
            }
            catch
            {
                // ignore
            }

            try
            {
                _mediaPlayer.Pause();
                _mediaPlayer.Time = 0;
            }
            catch
            {
                // ignore
            }

            if (!File.Exists(tempPath))
            {
                return null;
            }

            await using var stream = File.OpenRead(tempPath);
            return new Bitmap(stream);
        }
        finally
        {
            try
            {
                if (!wasPlaying)
                {
                    _mediaPlayer.Pause();
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    public void ClearContent(string title = "Idle")
    {
        ContentTitle = title;
        Initiative = null;
        ContentImage = null;
        ClearVideoInternal();
            OverlayEffects = OverlayEffectsState.None;
        IsSetup = true;
    }

    private void ClearVideoInternal()
    {
        _contentVideoPath = null;
        _loopVideo = false;
        _loopController.SetEnabled(false);
        _loopController.SetHasVideo(false);
        _loopController.SetArmed(false);
        _pendingSeekTimeMs = null;
        _pendingPrimeFrame = false;
        _loopController.ResetPending();

        UpdateLoopTimer();
        try
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Media = null;
        }
        catch
        {
            // ignore
        }

        try
        {
            _currentVideoMedia?.Dispose();
        }
        catch
        {
            // ignore
        }
        _currentVideoMedia = null;

        OnPropertyChanged(nameof(ContentVideoPath));
        OnPropertyChanged(nameof(HasVideo));
        OnPropertyChanged(nameof(IsShowingVideo));
        OnPropertyChanged(nameof(IsShowingText));
        OnPropertyChanged(nameof(IsShowingIdleLogo));
        OnPropertyChanged(nameof(IsShowingNonIdleText));
    }

    public bool TryGetVideoPixelSize(out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            uint w = 0;
            uint h = 0;
            if (!_mediaPlayer.Size(0, ref w, ref h))
            {
                return false;
            }

            if (w == 0 || h == 0)
            {
                return false;
            }

            width = (int)w;
            height = (int)h;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            _loopTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _loopTimer.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            _mediaPlayer.Stop();
        }
        catch
        {
            // ignore
        }

        try
        {
            _currentVideoMedia?.Dispose();
        }
        catch
        {
            // ignore
        }
        _currentVideoMedia = null;

        // Avoid disposing LibVLC objects here; depending on timing/window teardown,
        // native disposal can terminate the process (no managed exception to catch).
        try
        {
            _mediaPlayer.Media = null;
        }
        catch
        {
            // ignore
        }

        _contentImage?.Dispose();
        _contentImage = null;
    }
}
