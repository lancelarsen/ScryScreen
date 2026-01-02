using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScryScreen.App.Models;
using ScryScreen.Core.InitiativeTracker;

namespace ScryScreen.App.ViewModels;

public sealed partial class InitiativeTrackerViewModel : ViewModelBase
{
    private const int DefaultRowCount = 5;

    private const string UnnamedToken = "(Unnamed)";

    private bool _suppressRoundSync;

    private InitiativeTrackerState _state = InitiativeTrackerState.Empty;

    private bool _isReordering;

    public event Action? StateChanged;

    public InitiativeTrackerViewModel()
    {
        // Start with a few blank rows so the tracker feels ready immediately.
        for (var i = 0; i < DefaultRowCount; i++)
        {
            Entries.Add(CreateBlankEntry());
        }

        foreach (var entry in Entries)
        {
            entry.PropertyChanged += OnEntryPropertyChanged;
        }

        Entries.CollectionChanged += OnEntriesCollectionChanged;

        RebuildStateFromEntries(sort: true);
        ReorderCollectionToMatchState();
    }

    public ObservableCollection<InitiativeEntryViewModel> Entries { get; } = new();

    public bool IsEmpty => Entries.Count == 0;

    [ObservableProperty]
    private int round = 1;

    public IReadOnlyList<InitiativePortalFontSize> PortalFontSizes { get; } = new[]
    {
        InitiativePortalFontSize.Small,
        InitiativePortalFontSize.Medium,
        InitiativePortalFontSize.Large,
    };

    [ObservableProperty]
    private double overlayOpacity;

    [ObservableProperty]
    private InitiativePortalFontSize portalFontSize = InitiativePortalFontSize.Medium;

    partial void OnOverlayOpacityChanged(double value)
    {
        // Display-only; notify portal clients.
        StateChanged?.Invoke();
    }

    partial void OnPortalFontSizeChanged(InitiativePortalFontSize value)
    {
        // Display-only; notify portal clients.
        StateChanged?.Invoke();
    }

    public Guid? ActiveId => _state.ActiveId;

    public InitiativeTrackerState SnapshotState() => _state;

    public string PortalText => InitiativeTrackerFormatter.ToPortalText(_state);

    public string ExportConfigJson(bool indented = true)
    {
        var config = new InitiativeTrackerConfig
        {
            Round = Round,
            ActiveId = _state.ActiveId,
            OverlayOpacity = OverlayOpacity,
            PortalFontSize = PortalFontSize.ToString(),
            Entries = Entries.Select(e => new InitiativeTrackerConfigEntry
            {
                Id = e.Id,
                Name = e.Name ?? string.Empty,
                Initiative = e.Initiative ?? string.Empty,
                Mod = e.Mod ?? string.Empty,
                IsHidden = e.IsHidden,
                Notes = e.Notes,
            }).ToList(),
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = indented,
        });
    }

    public void ImportConfigJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var config = JsonSerializer.Deserialize<InitiativeTrackerConfig>(json);
        if (config is null)
        {
            return;
        }

        var parsedFontSize = InitiativePortalFontSize.Medium;
        if (!string.IsNullOrWhiteSpace(config.PortalFontSize)
            && Enum.TryParse(config.PortalFontSize, ignoreCase: true, out InitiativePortalFontSize fs))
        {
            parsedFontSize = fs;
        }

        OverlayOpacity = Math.Clamp(config.OverlayOpacity, 0.0, 1.0);
        PortalFontSize = parsedFontSize;

        // Rebuild collection from file.
        Entries.Clear();

        var seenIds = new HashSet<Guid>();
        foreach (var entry in config.Entries ?? new List<InitiativeTrackerConfigEntry>())
        {
            var id = entry.Id;
            if (id == Guid.Empty || !seenIds.Add(id))
            {
                id = Guid.NewGuid();
                seenIds.Add(id);
            }

            Entries.Add(new InitiativeEntryViewModel(id)
            {
                Name = entry.Name ?? string.Empty,
                Initiative = entry.Initiative ?? string.Empty,
                Mod = entry.Mod ?? string.Empty,
                IsHidden = entry.IsHidden,
                Notes = entry.Notes,
            });
        }

        if (Entries.Count == 0)
        {
            for (var i = 0; i < DefaultRowCount; i++)
            {
                Entries.Add(CreateBlankEntry());
            }
        }

        var round = Math.Max(1, config.Round);
        _suppressRoundSync = true;
        try
        {
            Round = round;
        }
        finally
        {
            _suppressRoundSync = false;
        }

        var activeId = config.ActiveId;
        if (activeId.HasValue && Entries.All(e => e.Id != activeId.Value))
        {
            activeId = null;
        }

        RebuildStateFromEntries(sort: false, activeIdOverride: activeId);
        UpdateLastEntryFlags();
        RaisePortalTextChanged();
    }

    private void OnEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsEmpty));

        if (e.OldItems is not null)
        {
            foreach (var o in e.OldItems.OfType<InitiativeEntryViewModel>())
            {
                o.PropertyChanged -= OnEntryPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var n in e.NewItems.OfType<InitiativeEntryViewModel>())
            {
                n.PropertyChanged += OnEntryPropertyChanged;
            }
        }

        RebuildStateFromEntries(sort: true);
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isReordering)
        {
            // Collection is being re-ordered to match sorted state; avoid loops.
            RebuildStateFromEntries(sort: false);
            return;
        }

        if (e.PropertyName is nameof(InitiativeEntryViewModel.IsActive))
        {
            // UI-only; driven by ActiveId.
            return;
        }

        if (e.PropertyName is nameof(InitiativeEntryViewModel.IsLast))
        {
            // UI-only; driven by collection ordering.
            return;
        }

        // Keep portal output updated immediately, but do not auto-sort/reorder while the user is typing.
        RebuildStateFromEntries(sort: false);
    }

    [RelayCommand]
    private void AddEntry()
    {
        Entries.Add(CreateBlankEntry());

        RebuildStateFromEntries(sort: true);
        ReorderCollectionToMatchState();
    }

    [RelayCommand]
    private void RemoveEntry(InitiativeEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        Entries.Remove(entry);
        RebuildStateFromEntries(sort: true);
        ReorderCollectionToMatchState();
    }

    [RelayCommand]
    private void ClearAll()
    {
        Entries.Clear();
        for (var i = 0; i < DefaultRowCount; i++)
        {
            Entries.Add(CreateBlankEntry());
        }

        _state = InitiativeTrackerState.Empty;
        Round = _state.Round;
        RebuildStateFromEntries(sort: true);
        ReorderCollectionToMatchState();
    }

    [RelayCommand]
    private void Sort()
    {
        RebuildStateFromEntries(sort: true);
        ReorderCollectionToMatchState();
    }

    [RelayCommand]
    private void NextTurn()
    {
        AdvanceTurn(forward: true);
    }

    [RelayCommand]
    private void PreviousTurn()
    {
        AdvanceTurn(forward: false);
    }

    private void AdvanceTurn(bool forward)
    {
        // Turn eligibility is based on UI field presence (so "0" counts as present).
        // Order is based on current sorted state.
        var vmById = Entries.ToDictionary(e => e.Id);

        bool IsEligible(InitiativeEntryViewModel vm)
        {
            if (vm.IsHidden)
            {
                return false;
            }

            var name = (vm.Name ?? string.Empty).Trim();
            if (name.Length == 0 || string.Equals(name, UnnamedToken, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // "Init present" = user typed something (including "0").
            return !string.IsNullOrWhiteSpace(vm.Initiative);
        }

        var eligibleIds = _state.Entries
            .Select(e => e.Id)
            .Where(id => vmById.TryGetValue(id, out var vm) && IsEligible(vm))
            .ToArray();

        if (eligibleIds.Length == 0)
        {
            _state = _state with { ActiveId = null };
            UpdateActiveFlags();
            RaisePortalTextChanged();
            return;
        }

        var currentIdx = -1;
        if (_state.ActiveId.HasValue)
        {
            for (var i = 0; i < eligibleIds.Length; i++)
            {
                if (eligibleIds[i] == _state.ActiveId.Value)
                {
                    currentIdx = i;
                    break;
                }
            }
        }

        var newRound = _state.Round;
        Guid newActive;

        if (currentIdx < 0)
        {
            newActive = forward ? eligibleIds[0] : eligibleIds[^1];
        }
        else if (forward)
        {
            var next = currentIdx + 1;
            if (next >= eligibleIds.Length)
            {
                newActive = eligibleIds[0];
                newRound = _state.Round + 1;
            }
            else
            {
                newActive = eligibleIds[next];
            }
        }
        else
        {
            var prev = currentIdx - 1;
            if (prev < 0)
            {
                newActive = eligibleIds[^1];
                newRound = Math.Max(1, _state.Round - 1);
            }
            else
            {
                newActive = eligibleIds[prev];
            }
        }

        _state = _state with { ActiveId = newActive, Round = newRound };

        // Update Round without invoking OnRoundChanged (we already updated state).
        _suppressRoundSync = true;
        try
        {
            Round = _state.Round;
        }
        finally
        {
            _suppressRoundSync = false;
        }

        UpdateActiveFlags();
        RaisePortalTextChanged();
    }

    [RelayCommand]
    private void ResetRound()
    {
        _state = InitiativeTrackerEngine.SetRound(_state, 1);
        Round = _state.Round;
        UpdateActiveFlags();
        RaisePortalTextChanged();
    }

    [RelayCommand]
    private void IncrementRound()
    {
        _state = InitiativeTrackerEngine.SetRound(_state, _state.Round + 1);
        Round = _state.Round;
        UpdateActiveFlags();
        RaisePortalTextChanged();
    }

    [RelayCommand]
    private void DecrementRound()
    {
        _state = InitiativeTrackerEngine.SetRound(_state, _state.Round - 1);
        Round = _state.Round;
        UpdateActiveFlags();
        RaisePortalTextChanged();
    }

    partial void OnRoundChanged(int value)
    {
        if (_suppressRoundSync)
        {
            return;
        }

        // Round can be changed via commands; normalize if it changes for any other reason.
        _state = InitiativeTrackerEngine.SetRound(_state, value);
        UpdateActiveFlags();
        RaisePortalTextChanged();
    }

    private void RebuildStateFromEntries(bool sort, Guid? activeIdOverride = null)
    {
        static int ParseIntOrZero(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            return int.TryParse(text, out var value) ? value : 0;
        }

        var entries = Entries.Select(vm => new InitiativeEntry(
                Id: vm.Id,
                Name: vm.Name,
                Initiative: ParseIntOrZero(vm.Initiative),
                Mod: ParseIntOrZero(vm.Mod),
                IsHidden: vm.IsHidden,
                Notes: vm.Notes))
            .ToArray();

        var next = new InitiativeTrackerState(
            Entries: entries,
            Round: Math.Max(1, Round),
            ActiveId: activeIdOverride ?? _state.ActiveId);

        next = InitiativeTrackerEngine.NormalizeState(next);
        if (sort)
        {
            next = InitiativeTrackerEngine.Sort(next);
        }

        _state = next;
        Round = _state.Round;
        UpdateActiveFlags();
        RaisePortalTextChanged();
    }

    private static InitiativeEntryViewModel CreateBlankEntry()
    {
        return new InitiativeEntryViewModel(Guid.NewGuid())
        {
            Name = string.Empty,
            Initiative = string.Empty,
            Mod = string.Empty,
            IsHidden = false,
            Notes = null,
        };
    }

    private void ReorderCollectionToMatchState()
    {
        UpdateLastEntryFlags();

        if (_state.Entries.Length <= 1 || Entries.Count <= 1)
        {
            return;
        }

        var currentById = Entries.ToDictionary(x => x.Id);
        var ordered = new List<InitiativeEntryViewModel>(_state.Entries.Length);
        foreach (var entry in _state.Entries)
        {
            if (currentById.TryGetValue(entry.Id, out var vm))
            {
                ordered.Add(vm);
            }
        }

        if (ordered.Count != Entries.Count)
        {
            // Defensive: if collection/state got out of sync, don't try to reorder.
            return;
        }

        _isReordering = true;
        try
        {
            Entries.Clear();
            foreach (var vm in ordered)
            {
                Entries.Add(vm);
            }
        }
        finally
        {
            _isReordering = false;
        }

        UpdateActiveFlags();
        UpdateLastEntryFlags();
    }

    private void UpdateLastEntryFlags()
    {
        for (var i = 0; i < Entries.Count; i++)
        {
            Entries[i].IsLast = i == Entries.Count - 1;
        }
    }

    private void UpdateActiveFlags()
    {
        var activeId = _state.ActiveId;
        foreach (var vm in Entries)
        {
            vm.IsActive = activeId.HasValue && vm.Id == activeId.Value;
        }
    }

    private void RaisePortalTextChanged()
    {
        OnPropertyChanged(nameof(PortalText));
        OnPropertyChanged(nameof(ActiveId));
        StateChanged?.Invoke();
    }
}
