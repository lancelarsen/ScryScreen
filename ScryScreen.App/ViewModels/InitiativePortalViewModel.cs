using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ScryScreen.Core.InitiativeTracker;

namespace ScryScreen.App.ViewModels;

public sealed partial class InitiativePortalViewModel : ObservableObject
{
    private const int MaxEntries = 14;

    private const string UnnamedToken = "(Unnamed)";

    public InitiativePortalViewModel(InitiativeTrackerState state)
    {
        Update(state);
    }

    [ObservableProperty]
    private int round;

    [ObservableProperty]
    private IReadOnlyList<InitiativePortalEntryViewModel> entries = Array.Empty<InitiativePortalEntryViewModel>();

    [ObservableProperty]
    private int hiddenOmittedCount;

    [ObservableProperty]
    private int additionalEntriesCount;

    public bool HasAdditionalEntries => AdditionalEntriesCount > 0;

    public void Update(InitiativeTrackerState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        Round = Math.Max(1, state.Round);

        var all = state.Entries ?? Array.Empty<InitiativeEntry>();
        var visible = all
            .Where(e =>
                !e.IsHidden
                && !string.IsNullOrWhiteSpace(e.Name)
                && !string.Equals(e.Name.Trim(), UnnamedToken, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        HiddenOmittedCount = all.Length - visible.Length;

        var activeId = state.ActiveId;

        var limited = visible
            .Take(MaxEntries)
            .Select(e => new InitiativePortalEntryViewModel(
                Id: e.Id,
                Name: e.Name.Trim(),
                Initiative: e.Initiative,
                Mod: e.Mod,
                IsActive: activeId.HasValue && e.Id == activeId.Value))
            .ToArray();

        Entries = limited;
        AdditionalEntriesCount = Math.Max(0, visible.Length - limited.Length);

        OnPropertyChanged(nameof(HasAdditionalEntries));
    }
}

public sealed record InitiativePortalEntryViewModel(
    Guid Id,
    string Name,
    int Initiative,
    int Mod,
    bool IsActive)
{
    public bool HasMod => Mod != 0;

    public string InitiativeDisplay => Initiative.ToString();

    public string ModDisplay
    {
        get
        {
            if (Mod == 0)
            {
                return string.Empty;
            }

            var modText = Mod > 0 ? $"+{Mod}" : Mod.ToString();
            return $"({modText})";
        }
    }
}
