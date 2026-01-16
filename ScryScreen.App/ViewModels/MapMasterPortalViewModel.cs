using CommunityToolkit.Mvvm.ComponentModel;
using ScryScreen.App.Models;
using Avalonia.Media.Imaging;

namespace ScryScreen.App.ViewModels;

public partial class MapMasterPortalViewModel : ViewModelBase
{
    public MapMasterPortalViewModel(MapMasterOverlayState state)
    {
        Update(state);
    }

    [ObservableProperty]
    private double overlayOpacity;

    [ObservableProperty]
    private WriteableBitmap? maskBitmap;

    public bool HasMask => MaskBitmap is not null;

    partial void OnMaskBitmapChanged(WriteableBitmap? value) => OnPropertyChanged(nameof(HasMask));

    public void Update(MapMasterOverlayState state)
    {
        OverlayOpacity = state.OverlayOpacity;

        if (ReferenceEquals(MaskBitmap, state.MaskBitmap))
        {
            // Bitmap pixels were edited in-place; force bindings to refresh.
            OnPropertyChanged(nameof(MaskBitmap));
        }
        else
        {
            MaskBitmap = state.MaskBitmap;
        }
    }
}
