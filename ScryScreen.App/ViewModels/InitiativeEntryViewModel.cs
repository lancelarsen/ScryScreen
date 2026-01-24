using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ScryScreen.App.Models;

namespace ScryScreen.App.ViewModels;

public sealed partial class InitiativeEntryViewModel : ViewModelBase
{
    public InitiativeEntryViewModel(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string playerName = string.Empty;

    [ObservableProperty]
    private string initiative = string.Empty;

    [ObservableProperty]
    private string mod = string.Empty;

    [ObservableProperty]
    private bool isHidden;

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    private bool isLast;

    [ObservableProperty]
    private string? notes;

    [ObservableProperty]
    private string maxHp = string.Empty;

    [ObservableProperty]
    private string currentHp = string.Empty;

    [ObservableProperty]
    private string armorClass = string.Empty;

    [ObservableProperty]
    private string passivePerception = string.Empty;

    public ObservableCollection<InitiativeEntryConditionViewModel> Conditions { get; } = new();

    [ObservableProperty]
    private ConditionDefinition? selectedConditionToAdd;

    [ObservableProperty]
    private int? selectedConditionRoundsToAdd;

    partial void OnSelectedConditionToAddChanged(ConditionDefinition? value)
    {
        // Default duration to "-" (no timer) whenever a new condition is selected.
        SelectedConditionRoundsToAdd = null;
    }
}
