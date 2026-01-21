using CommunityToolkit.Mvvm.ComponentModel;
using ScryScreen.App.Models;
using System;
using System.Collections.Generic;

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

    [ObservableProperty]
    private long rollId;

    [ObservableProperty]
    private IReadOnlyList<DiceDieRotation> rotations = Array.Empty<DiceDieRotation>();

    [ObservableProperty]
    private DiceRollRequest? rollRequest;

    [ObservableProperty]
    private IReadOnlyList<DiceDieVisualConfig> visualConfigs = Array.Empty<DiceDieVisualConfig>();

    [ObservableProperty]
    private long visualConfigRevision;

    [ObservableProperty]
    private long clearDiceId;

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    partial void OnTextChanged(string value) => OnPropertyChanged(nameof(HasText));

    public void Update(DiceRollerState state)
    {
        Text = state.Text;
        OverlayOpacity = state.OverlayOpacity;
        RollId = state.RollId;
        Rotations = state.Rotations ?? Array.Empty<DiceDieRotation>();
        RollRequest = state.RollRequest;
        VisualConfigs = state.VisualConfigs ?? Array.Empty<DiceDieVisualConfig>();
        VisualConfigRevision = state.VisualConfigRevision;
        ClearDiceId = state.ClearDiceId;
    }
}
