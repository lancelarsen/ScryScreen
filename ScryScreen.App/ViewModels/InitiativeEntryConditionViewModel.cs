using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScryScreen.App.ViewModels;

public sealed partial class InitiativeEntryConditionViewModel : ViewModelBase
{
    public InitiativeEntryConditionViewModel(
        InitiativeEntryViewModel owner,
        Guid conditionId,
        string shortTag,
        string colorHex,
        bool isManualOnly,
        int? roundsRemaining)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ConditionId = conditionId;
        ShortTag = shortTag ?? string.Empty;
        ColorHex = colorHex ?? "#FFFFFFFF";
        IsManualOnly = isManualOnly;
        this.roundsRemaining = roundsRemaining;
        UpdateBrush();
    }

    public InitiativeEntryViewModel Owner { get; }

    public Guid ConditionId { get; }

    public bool IsManualOnly { get; }

    [ObservableProperty]
    private string shortTag;

    [ObservableProperty]
    private string colorHex;

    [ObservableProperty]
    private IBrush colorBrush = Brushes.White;

    [ObservableProperty]
    private int? roundsRemaining;

    public bool HasTimer => RoundsRemaining.HasValue;

    partial void OnColorHexChanged(string value)
    {
        UpdateBrush();
    }

    partial void OnRoundsRemainingChanged(int? value)
    {
        OnPropertyChanged(nameof(HasTimer));
    }

    private void UpdateBrush()
    {
        try
        {
            ColorBrush = Brush.Parse(ColorHex);
        }
        catch
        {
            ColorBrush = Brushes.White;
        }
    }
}
