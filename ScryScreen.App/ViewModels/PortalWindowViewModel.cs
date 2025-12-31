using System;
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

        _libVlc = new LibVLC();
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
        }
    }

    public void SetVideoOptions(bool autoPlay, bool loop)
    {
        _loopVideo = loop;

        if (!HasVideo)
        {
            return;
        }

        try
        {
            if (autoPlay)
            {
                if (!_mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.Play();
                }
            }
        }
        catch
        {
            // ignore
        }
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
