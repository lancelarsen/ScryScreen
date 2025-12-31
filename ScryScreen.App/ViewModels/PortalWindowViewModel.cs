using System;
using System.IO;
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
    private Media? _currentVideoMedia;
    private string? _contentVideoPath;
    private bool _loopVideo;
    private bool _autoPlayRequested;
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
        _mediaPlayer.EndReached += (_, _) =>
        {
            if (_loopVideo)
            {
                try
                {
                    _mediaPlayer.Stop();
                    _mediaPlayer.Play();
                }
                catch
                {
                    // ignore playback errors
                }
            }
        };
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

        _autoPlayRequested = false;
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

    public void SetVideo(string filePath, string title, bool autoPlay, bool loop)
    {
        ContentTitle = title;
        ContentImage = null;
        _contentVideoPath = filePath;
        _loopVideo = loop;
        _autoPlayRequested = autoPlay;
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
            _autoPlayRequested = false;
        }

        OnPropertyChanged(nameof(ContentVideoPath));
        OnPropertyChanged(nameof(HasVideo));
        OnPropertyChanged(nameof(IsShowingVideo));
        OnPropertyChanged(nameof(IsShowingText));

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

    public void TryStartVideoIfNeeded()
    {
        if (!_autoPlayRequested || !HasVideo)
        {
            return;
        }

        try
        {
            if (!_mediaPlayer.IsPlaying)
            {
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
                }

                _mediaPlayer.Play();
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _autoPlayRequested = false;
            _pendingPrimeFrame = false;
            _pendingSeekTimeMs = null;
        }
    }

    public void SetVideoOptions(bool autoPlay, bool loop)
    {
        _loopVideo = loop;

        // Intentionally do not auto-start playback here.
        // Auto-Play is used only at assignment time; videos should start paused by default.
    }

    public void SetVideoLoop(bool loop)
    {
        _loopVideo = loop;
    }

    public bool ToggleVideoPlayPause()
    {
        if (!HasVideo)
        {
            return false;
        }

        _autoPlayRequested = false;

        try
        {
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
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

        _autoPlayRequested = false;

        try
        {
            _mediaPlayer.Time = 0;
            _mediaPlayer.Play();
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
        _autoPlayRequested = false;
        _pendingSeekTimeMs = null;
        _pendingPrimeFrame = false;
        _isPriming = false;
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
