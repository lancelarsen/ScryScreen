using System;
using CommunityToolkit.Mvvm.ComponentModel;

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
    private string initiative = string.Empty;

    [ObservableProperty]
    private string mod = string.Empty;

    [ObservableProperty]
    private bool isHidden;

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    private string? notes;
}
