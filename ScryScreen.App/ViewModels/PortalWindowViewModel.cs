using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LibVLCSharp.Shared;

namespace ScryScreen.App.ViewModels;

public partial class PortalWindowViewModel : ViewModelBase, IDisposable
{
    private Bitmap? _contentImage;
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly Timer _loopTimer;
    private int _loopRestartRequested;
    private int _isLoopRestarting;
    private int _suppressEndReached;
    private long _loopNextRestartTicks;
    private int _loopRestartAttempts;
    private bool _loopArmed;
    private Media? _currentVideoMedia;
    private string? _contentVideoPath;
    private bool _loopVideo;
    private long? _pendingSeekTimeMs;
    private bool _pendingPrimeFrame;
    private bool _isPriming;

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
        // EndReached is raised on a LibVLC thread; do not do heavy/re-entrant operations here.
        // Just signal the loop timer to restart safely.
        _mediaPlayer.EndReached += (_, _) =>
        {
            if (!_loopVideo)
            {
                return;
            }

            // If the user isn't actively playing (or we're currently restarting), do not auto-loop.
            if (!_loopArmed || Interlocked.CompareExchange(ref _suppressEndReached, 0, 0) == 1)
            {
                return;
            }

            // Arm a restart; the timer will perform the actual Stop/Play off the LibVLC callback thread.
            Interlocked.Exchange(ref _loopRestartRequested, 1);
            Interlocked.Exchange(ref _loopNextRestartTicks, DateTime.UtcNow.Ticks);
        };

        // Timer-based loop restart to avoid calling Stop/Play inside EndReached.
        // The timer does nothing unless a restart is requested.
        _loopTimer = new Timer(_ => LoopTick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private static long UtcNowTicks() => DateTime.UtcNow.Ticks;

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

        // Only do work when a restart has been requested.
        if (Interlocked.CompareExchange(ref _loopRestartRequested, 0, 0) == 0)
        {
            return;
        }

        // Respect backoff.
        var nextTicks = Interlocked.Read(ref _loopNextRestartTicks);
        if (UtcNowTicks() < nextTicks)
        {
            return;
        }

        // Ensure only one restart attempt runs at a time.
        if (Interlocked.Exchange(ref _isLoopRestarting, 1) == 1)
        {
            return;
        }

        try
        {
            // If we don't have a native target yet, don't trigger VLC's fallback window.
            if (OperatingSystem.IsWindows() && _mediaPlayer.Hwnd == IntPtr.Zero)
            {
                Interlocked.Exchange(ref _loopNextRestartTicks, DateTime.UtcNow.AddMilliseconds(250).Ticks);
                return;
            }

            if (!_loopArmed)
            {
                // User isn't actively playing; do not loop.
                Interlocked.Exchange(ref _loopRestartRequested, 0);
                _loopRestartAttempts = 0;
                return;
            }

            var restarted = TryRestartLoopPlayback();
            if (restarted)
            {
                Interlocked.Exchange(ref _loopRestartRequested, 0);
                _loopRestartAttempts = 0;
                Interlocked.Exchange(ref _loopNextRestartTicks, UtcNowTicks());
            }
            else
            {
                _loopRestartAttempts++;
                var delayMs = Math.Min(1500, 200 + (int)(Math.Pow(2, Math.Min(3, _loopRestartAttempts)) * 75));
                Interlocked.Exchange(ref _loopNextRestartTicks, DateTime.UtcNow.AddMilliseconds(delayMs).Ticks);
            }
        }
        catch
        {
            // ignore
            _loopRestartAttempts++;
            Interlocked.Exchange(ref _loopNextRestartTicks, DateTime.UtcNow.AddMilliseconds(500).Ticks);
        }
        finally
        {
            Interlocked.Exchange(ref _isLoopRestarting, 0);
        }
    }

    private bool TryRestartLoopPlayback()
    {
        // Designed to be called from the loop timer callback.
        // Avoid re-entrancy and suppress EndReached while we manipulate the player.
        Interlocked.Exchange(ref _suppressEndReached, 1);
        try
        {
            // Step 1: Stop to reset "ended" state reliably.
            try
            {
                _mediaPlayer.Stop();
            }
            catch
            {
                // ignore
            }

            Thread.Sleep(25);

            // Step 2: Force start position.
            try
            {
                _mediaPlayer.Time = 0;
            }
            catch
            {
                // ignore
            }

            Thread.Sleep(25);

            // Step 3: Try to play using current media.
            try
            {
                if (_mediaPlayer.Play())
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            // Step 4: Re-assign the media and retry.
            try
            {
                if (_currentVideoMedia is not null)
                {
                    _mediaPlayer.Media = _currentVideoMedia;
                    if (_mediaPlayer.Play())
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            // Step 5: As a last resort, recreate the Media instance.
            try
            {
                if (!string.IsNullOrWhiteSpace(_contentVideoPath))
                {
                    var oldMedia = _currentVideoMedia;
                    _currentVideoMedia = new Media(_libVlc, new Uri(_contentVideoPath));
                    _mediaPlayer.Media = _currentVideoMedia;

                    try
                    {
                        oldMedia?.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }

                    if (_mediaPlayer.Play())
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _suppressEndReached, 0);
        }
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
        }
    }

    public bool HasImage => ContentImage is not null;

    public bool HasVideo => !string.IsNullOrWhiteSpace(_contentVideoPath);

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
            if (OperatingSystem.IsWindows() && _mediaPlayer.Hwnd != IntPtr.Zero)
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

    public bool IsShowingText => !IsSetup && !HasImage && !HasVideo;

    [ObservableProperty]
    private bool isSetup;

    partial void OnIsSetupChanged(bool value)
    {
        OnPropertyChanged(nameof(IsShowingImage));
        OnPropertyChanged(nameof(IsShowingVideo));
        OnPropertyChanged(nameof(IsShowingText));
    }

    [ObservableProperty]
    private bool isIdentifyOverlayVisible;

    [ObservableProperty]
    private MediaScaleMode scaleMode;

    [ObservableProperty]
    private MediaAlign align;

    public void ShowIdentifyOverlay() => IsIdentifyOverlayVisible = true;

    public void HideIdentifyOverlay() => IsIdentifyOverlayVisible = false;

    public void SetImage(Bitmap bitmap, string title)
    {
        ContentTitle = title;
        ClearVideoInternal();
        ContentImage = bitmap;
    }

    public void SetVideo(string filePath, string title, bool loop)
    {
        ContentTitle = title;
        ContentImage = null;
        _contentVideoPath = filePath;
        _loopVideo = loop;
        _loopArmed = false;
        _loopRestartAttempts = 0;
        Interlocked.Exchange(ref _loopRestartRequested, 0);
        Interlocked.Exchange(ref _loopNextRestartTicks, UtcNowTicks());
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
        if (_isPriming)
        {
            return;
        }

        if (!HasVideo || _mediaPlayer.IsPlaying)
        {
            return;
        }

        if (!_pendingPrimeFrame || !_pendingSeekTimeMs.HasValue)
        {
            return;
        }

        // Only safe to decode a frame once we have a native render target.
        if (OperatingSystem.IsWindows() && _mediaPlayer.Hwnd == IntPtr.Zero)
        {
            return;
        }

        _isPriming = true;
        var target = _pendingSeekTimeMs.Value;

        var originalMute = false;
        var originalVolume = 0;
        try
        {
            originalMute = _mediaPlayer.Mute;
            originalVolume = _mediaPlayer.Volume;
        }
        catch
        {
            // ignore
        }

        try
        {
            try
            {
                _mediaPlayer.Mute = true;
            }
            catch
            {
                // ignore
            }

            try
            {
                if (target < 0) target = 0;
                _mediaPlayer.Time = target;
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

            await Task.Delay(120).ConfigureAwait(false);

            try
            {
                _mediaPlayer.Pause();
            }
            catch
            {
                // ignore
            }

            try
            {
                _mediaPlayer.Time = target;
            }
            catch
            {
                // ignore
            }

            _pendingPrimeFrame = false;
        }
        finally
        {
            try
            {
                _mediaPlayer.Mute = originalMute;
                _mediaPlayer.Volume = originalVolume;
            }
            catch
            {
                // ignore
            }

            _isPriming = false;
        }
    }

    public void SetVideoLoop(bool loop)
    {
        _loopVideo = loop;

        if (!loop)
        {
            Interlocked.Exchange(ref _loopRestartRequested, 0);
            _loopRestartAttempts = 0;
            Interlocked.Exchange(ref _loopNextRestartTicks, UtcNowTicks());
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
                _loopArmed = false;
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
            _loopArmed = true;
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
        _loopArmed = false;
        Interlocked.Exchange(ref _loopRestartRequested, 0);
        _loopRestartAttempts = 0;

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
        ContentImage = null;
        ClearVideoInternal();
        IsSetup = true;
    }

    private void ClearVideoInternal()
    {
        _contentVideoPath = null;
        _loopVideo = false;
        _loopArmed = false;
        _pendingSeekTimeMs = null;
        _pendingPrimeFrame = false;
        _isPriming = false;
        Interlocked.Exchange(ref _loopRestartRequested, 0);
        _loopRestartAttempts = 0;
        Interlocked.Exchange(ref _loopNextRestartTicks, UtcNowTicks());

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

        _mediaPlayer.Dispose();
        _libVlc.Dispose();

        _contentImage?.Dispose();
        _contentImage = null;
    }
}
