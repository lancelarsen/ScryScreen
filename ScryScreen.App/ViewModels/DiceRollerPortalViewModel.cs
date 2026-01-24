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
    private double overlayOpacity = 0.65;

    [ObservableProperty]
    private long rollId;

    [ObservableProperty]
    private IReadOnlyList<DiceDieRotation> rotations = Array.Empty<DiceDieRotation>();

    [ObservableProperty]
    private IReadOnlyList<DiceRollRequest> rollRequests = Array.Empty<DiceRollRequest>();

    [ObservableProperty]
    private IReadOnlyList<DiceDieVisualConfig> visualConfigs = Array.Empty<DiceDieVisualConfig>();

    [ObservableProperty]
    private long visualConfigRevision;

    [ObservableProperty]
    private long clearDiceId;

    [ObservableProperty]
    private bool debugVisible;

    [ObservableProperty]
    private bool resultsVisible = true;

    [ObservableProperty]
    private DiceRollerResultFontSize resultFontSize = DiceRollerResultFontSize.Medium;

    public double ResultFontPixelSize => ResultFontSize switch
    {
        DiceRollerResultFontSize.Small => 26,
        DiceRollerResultFontSize.Medium => 34,
        _ => 42,
    };

    public bool HasVisibleText => ResultsVisible && HasText;

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasText));
        OnPropertyChanged(nameof(HasVisibleText));
    }

    public void Update(DiceRollerState state)
    {
        Text = state.Text;
        RollId = state.RollId;
        Rotations = state.Rotations ?? Array.Empty<DiceDieRotation>();
        VisualConfigs = state.VisualConfigs ?? Array.Empty<DiceDieVisualConfig>();
        VisualConfigRevision = state.VisualConfigRevision;
        OverlayOpacity = state.OverlayOpacity;
        ClearDiceId = state.ClearDiceId;
        RollRequests = state.RollRequests ?? Array.Empty<DiceRollRequest>();
        DebugVisible = state.DebugVisible;
        ResultsVisible = state.ResultsVisible;
        ResultFontSize = state.ResultFontSize;
    }

    partial void OnResultFontSizeChanged(DiceRollerResultFontSize value)
        => OnPropertyChanged(nameof(ResultFontPixelSize));

    partial void OnResultsVisibleChanged(bool value)
        => OnPropertyChanged(nameof(HasVisibleText));
}
