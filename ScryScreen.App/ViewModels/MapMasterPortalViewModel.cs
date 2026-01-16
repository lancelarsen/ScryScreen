using CommunityToolkit.Mvvm.ComponentModel;
using ScryScreen.App.Models;

namespace ScryScreen.App.ViewModels;

public partial class MapMasterPortalViewModel : ViewModelBase
{
    public MapMasterPortalViewModel(MapMasterState state)
    {
        Update(state);
    }

    [ObservableProperty]
    private double overlayOpacity;

    [ObservableProperty]
    private double revealX;

    [ObservableProperty]
    private double revealY;

    [ObservableProperty]
    private double revealWidth;

    [ObservableProperty]
    private double revealHeight;

    public void Update(MapMasterState state)
    {
        OverlayOpacity = state.OverlayOpacity;
        RevealX = state.RevealX;
        RevealY = state.RevealY;
        RevealWidth = state.RevealWidth;
        RevealHeight = state.RevealHeight;
    }
}
