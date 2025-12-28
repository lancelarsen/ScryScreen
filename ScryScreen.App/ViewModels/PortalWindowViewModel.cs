using CommunityToolkit.Mvvm.ComponentModel;

namespace ScryScreen.App.ViewModels;

public partial class PortalWindowViewModel : ViewModelBase
{
    public PortalWindowViewModel(int portalNumber)
    {
        PortalNumber = portalNumber;
        ScreenName = "Unassigned";
        ContentTitle = "Idle";
        IsContentVisible = true;
        IsSetup = true;
        IsIdentifyOverlayVisible = false;
    }

    public int PortalNumber { get; }

    [ObservableProperty]
    private string screenName = string.Empty;

    [ObservableProperty]
    private bool isContentVisible;

    [ObservableProperty]
    private string contentTitle = string.Empty;

    [ObservableProperty]
    private bool isSetup;

    [ObservableProperty]
    private bool isIdentifyOverlayVisible;

    public void ShowIdentifyOverlay() => IsIdentifyOverlayVisible = true;

    public void HideIdentifyOverlay() => IsIdentifyOverlayVisible = false;
}
