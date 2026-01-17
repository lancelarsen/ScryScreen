using CommunityToolkit.Mvvm.ComponentModel;
using ScryScreen.App.Models;
using Avalonia.Media.Imaging;
using Avalonia.Media;

namespace ScryScreen.App.ViewModels;

public partial class MapMasterPortalViewModel : ViewModelBase
{
    public MapMasterPortalViewModel(MapMasterOverlayState state)
    {
        Update(state);
    }

    [ObservableProperty]
    private double playerMaskOpacity;

    [ObservableProperty]
    private MapMasterMaskType maskType;

    [ObservableProperty]
    private WriteableBitmap? maskBitmap;

    public bool HasMask => MaskBitmap is not null;

    public bool IsSolidBlackMask => MaskType == MapMasterMaskType.Black;

    public IImage? MaskTexture => MapMasterMaskAssets.GetTexture(MaskType);

    partial void OnMaskBitmapChanged(WriteableBitmap? value) => OnPropertyChanged(nameof(HasMask));

    partial void OnMaskTypeChanged(MapMasterMaskType value)
    {
        OnPropertyChanged(nameof(IsSolidBlackMask));
        OnPropertyChanged(nameof(MaskTexture));
    }

    public void Update(MapMasterOverlayState state)
    {
        PlayerMaskOpacity = state.PlayerMaskOpacity;
        MaskType = state.MaskType;

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
