using CommunityToolkit.Mvvm.ComponentModel;
using ScryScreen.App.Models;

namespace ScryScreen.App.ViewModels;

public partial class DiceRollerPortalViewModel : ViewModelBase
{
    public DiceRollerPortalViewModel(DiceRollerState state)
    {
        Update(state);
    }

    [ObservableProperty]
    private string text = string.Empty;

    [ObservableProperty]
    private double overlayOpacity = 0.85;

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    partial void OnTextChanged(string value) => OnPropertyChanged(nameof(HasText));

    public void Update(DiceRollerState state)
    {
        Text = state.Text;
        OverlayOpacity = state.OverlayOpacity;
    }
}
