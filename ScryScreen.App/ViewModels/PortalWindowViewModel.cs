using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;

namespace ScryScreen.App.ViewModels;

public partial class PortalWindowViewModel : ViewModelBase
{
    private Bitmap? _contentImage;

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
        }
    }

    public bool HasImage => ContentImage is not null;

    public bool IsShowingImage => !IsSetup && HasImage;

    public bool IsShowingText => !IsSetup && !HasImage;

    [ObservableProperty]
    private bool isSetup;

    partial void OnIsSetupChanged(bool value)
    {
        OnPropertyChanged(nameof(IsShowingImage));
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
        ContentImage = bitmap;
    }

    public void ClearContent(string title = "Idle")
    {
        ContentTitle = title;
        ContentImage = null;
        IsSetup = true;
    }
}
