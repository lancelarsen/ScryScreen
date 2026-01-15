using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScryScreen.App.Models;
using ScryScreen.App.Services;
using ScryScreen.Core.InitiativeTracker;

namespace ScryScreen.App.ViewModels;

public sealed partial class InitiativeTrackerViewModel : ViewModelBase
{
    private const int DefaultRowCount = 5;

    private const string UnnamedToken = "(Unnamed)";

    private bool _suppressRoundSync;

    private InitiativeTrackerState _state = InitiativeTrackerState.Empty;

    private bool _isReordering;

    private bool _suppressEntrySync;

    public event Action? StateChanged;

    public InitiativeTrackerViewModel()
    {
        ConditionLibrary = ConditionLibraryService.LoadOrCreateDefault();
        RefreshConditionLibraryItems();

        // Start with a few blank rows so the tracker feels ready immediately.
        for (var i = 0; i < DefaultRowCount; i++)
        {
            Entries.Add(CreateBlankEntry());
        }

        foreach (var entry in Entries)
        {
            HookEntry(entry);
        }

        Entries.CollectionChanged += OnEntriesCollectionChanged;

        RebuildStateFromEntries(sort: true);
        ReorderCollectionToMatchState();
    }

    public ObservableCollection<InitiativeEntryViewModel> Entries { get; } = new();

    public ConditionLibraryService ConditionLibrary { get; }

    public IReadOnlyList<ConditionDefinition> ConditionDefinitions
        => ConditionLibrary.GetAllDefinitionsAlphabetical();

    public IReadOnlyList<int?> ConditionDurationOptions { get; } = new int?[] { null }
        .Concat(Enumerable.Range(1, 10).Select(x => (int?)x))
        .ToArray();

    public ObservableCollection<ConditionDefinitionViewModel> ConditionLibraryItems { get; } = new();

    public ObservableCollection<ColorSwatchViewModel> ConditionColorSwatches { get; } = new()
    {
        new("#FF000000"), new("#FF111827"), new("#FF374151"), new("#FF6B7280"), new("#FF9CA3AF"), new("#FFFFFFFF"),
        new("#FF7F1D1D"), new("#FFDC2626"), new("#FFF97316"), new("#FFF59E0B"), new("#FFEAB308"), new("#FF84CC16"),
        new("#FF22C55E"), new("#FF4ADE80"), new("#FF14B8A6"), new("#FF06B6D4"), new("#FF0EA5E9"), new("#FF3B82F6"),
        new("#FF6366F1"), new("#FF8B5CF6"), new("#FFA855F7"), new("#FFEC4899"), new("#FFFF4FD8"), new("#FFD4AF37"),
    };

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

    [ObservableProperty]
    private bool showConditionsConfiguration;

    [ObservableProperty]
    private ConditionDefinitionViewModel? selectedCondition;

    [ObservableProperty]
    private string selectedConditionNameText = string.Empty;

    [ObservableProperty]
    private string selectedConditionShortTagText = string.Empty;

    [ObservableProperty]
    private string selectedConditionColorHexText = "#FFFFFFFF";

    [ObservableProperty]
    private string selectedColorRText = "255";

    [ObservableProperty]
    private string selectedColorGText = "255";

    [ObservableProperty]
    private string selectedColorBText = "255";

    [ObservableProperty]
    private string newCustomConditionNameText = string.Empty;

    [ObservableProperty]
    private string newCustomConditionShortTagText = string.Empty;

    [ObservableProperty]
    private string newCustomConditionColorHexText = "#FFFFFFFF";

    [ObservableProperty]
    private string newCustomColorRText = "255";

    [ObservableProperty]
    private string newCustomColorGText = "255";

    [ObservableProperty]
    private string newCustomColorBText = "255";

    public bool HasSelectedCondition => SelectedCondition is not null;
    public bool CanDeleteSelectedCondition => SelectedCondition is not null && SelectedCondition.IsCustom;
    public bool CanEditSelectedConditionIdentity => SelectedCondition is not null && SelectedCondition.CanEditIdentity;

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

    partial void OnSelectedConditionChanged(ConditionDefinitionViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedCondition));
        OnPropertyChanged(nameof(CanDeleteSelectedCondition));
        OnPropertyChanged(nameof(CanEditSelectedConditionIdentity));

        DeleteSelectedCustomConditionCommand.NotifyCanExecuteChanged();

        if (value is null)
        {
            SelectedConditionNameText = string.Empty;
            SelectedConditionShortTagText = string.Empty;
            SelectedConditionColorHexText = "#FFFFFFFF";
            SelectedColorRText = "255";
            SelectedColorGText = "255";
            SelectedColorBText = "255";
            return;
        }

        SelectedConditionNameText = value.Name;
        SelectedConditionShortTagText = value.ShortTag;
        SelectedConditionColorHexText = value.ColorHex;
        UpdateSelectedRgbFromHex(value.ColorHex);
    }

    public Guid? ActiveId => _state.ActiveId;

    public InitiativeTrackerState SnapshotState() => _state;

    public string PortalText => InitiativeTrackerFormatter.ToPortalText(_state);

    public string PrevTurnDetailsText => BuildPrevTurnPreviewDetails();

    public string UpTurnDetailsText => BuildTurnPreviewDetails(offsetFromUp: 0);

    public string NextTurnDetailsText => BuildTurnPreviewDetails(offsetFromUp: 1);

    public string UpTurnLineText => $"Up: {UpTurnDetailsText}";

    public string NextTurnLineText => $"Next: {NextTurnDetailsText}";

    public string ExportConfigJson(bool indented = true)
    {
        var config = new InitiativeTrackerConfig
        {
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
                MaxHp = e.MaxHp ?? string.Empty,
                CurrentHp = e.CurrentHp ?? string.Empty,
                Conditions = e.Conditions
                    .Select(c => new InitiativeTrackerConfigEntryCondition
                    {
                        ConditionId = c.ConditionId,
                        RoundsRemaining = c.RoundsRemaining,
                    })
                    .ToList(),
            }).ToList(),
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = indented,
        });
    }

    [RelayCommand]
    private void SelectColorSwatchForSelected(ColorSwatchViewModel? swatch)
    {
        if (SelectedCondition is null || swatch is null)
        {
            return;
        }

        SelectedConditionColorHexText = swatch.Hex;
        SaveSelectedConditionEdits();
    }

    [RelayCommand]
    private void ApplySelectedRgb()
    {
        if (SelectedCondition is null)
        {
            return;
        }

        if (!TryBuildHexFromRgb(SelectedColorRText, SelectedColorGText, SelectedColorBText, out var hex))
        {
            return;
        }

        SelectedConditionColorHexText = hex;
        SaveSelectedConditionEdits();
    }

    [RelayCommand]
    private void SaveSelectedConditionEdits()
    {
        var selected = SelectedCondition;
        if (selected is null)
        {
            return;
        }

        if (!TryNormalizeHexColor(SelectedConditionColorHexText, out var normalizedColor))
        {
            return;
        }

        // Built-ins: only color is editable.
        if (selected.IsBuiltIn)
        {
            ConditionLibrary.SetColor(selected.Id, normalizedColor);
            ConditionLibrary.Save();
            RefreshConditionLibraryItems(keepSelectedId: selected.Id);
            RefreshAllEntryConditionAppearance();
            RaisePortalTextChanged();
            return;
        }

        // Customs: name + tag + color.
        var name = (SelectedConditionNameText ?? string.Empty).Trim();
        var tag = (SelectedConditionShortTagText ?? string.Empty).Trim();
        if (name.Length == 0 || tag.Length == 0)
        {
            return;
        }

        if (!ConditionLibrary.TryUpdateCustom(selected.Id, name, tag, normalizedColor))
        {
            return;
        }
        ConditionLibrary.Save();
        RefreshConditionLibraryItems(keepSelectedId: selected.Id);
        RefreshAllEntryConditionAppearance();
        RaisePortalTextChanged();
    }

    [RelayCommand]
    private void SelectColorSwatchForNewCustom(ColorSwatchViewModel? swatch)
    {
        if (swatch is null)
        {
            return;
        }

        NewCustomConditionColorHexText = swatch.Hex;
        UpdateNewCustomRgbFromHex(swatch.Hex);
    }

    [RelayCommand]
    private void ApplyNewCustomRgb()
    {
        if (!TryBuildHexFromRgb(NewCustomColorRText, NewCustomColorGText, NewCustomColorBText, out var hex))
        {
            return;
        }

        NewCustomConditionColorHexText = hex;
    }

    [RelayCommand]
    private void AddCustomCondition()
    {
        var name = (NewCustomConditionNameText ?? string.Empty).Trim();
        var tag = (NewCustomConditionShortTagText ?? string.Empty).Trim();

        if (name.Length == 0 || tag.Length == 0)
        {
            return;
        }

        if (!TryNormalizeHexColor(NewCustomConditionColorHexText, out var normalizedColor))
        {
            return;
        }

        var def = ConditionLibrary.AddCustom(name, tag, normalizedColor);
        ConditionLibrary.Save();

        NewCustomConditionNameText = string.Empty;
        NewCustomConditionShortTagText = string.Empty;
        NewCustomConditionColorHexText = "#FFFFFFFF";
        NewCustomColorRText = "255";
        NewCustomColorGText = "255";
        NewCustomColorBText = "255";

        RefreshConditionLibraryItems(keepSelectedId: def.Id);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedCondition))]
    private void DeleteSelectedCustomCondition()
    {
        var selected = SelectedCondition;
        if (selected is null || selected.IsBuiltIn)
        {
            return;
        }

        if (!ConditionLibrary.DeleteCustom(selected.Id))
        {
            return;
        }

        ConditionLibrary.Save();
        PurgeConditionFromAllEntries(selected.Id);
        RefreshConditionLibraryItems(keepSelectedId: null);
        RaisePortalTextChanged();
    }

    private void RefreshConditionLibraryItems(Guid? keepSelectedId = null)
    {
        ConditionLibraryItems.Clear();
        foreach (var def in ConditionLibrary.GetAllDefinitionsAlphabetical())
        {
            ConditionLibraryItems.Add(new ConditionDefinitionViewModel(def));
        }

        var toSelect = keepSelectedId;
        if (toSelect.HasValue)
        {
            SelectedCondition = ConditionLibraryItems.FirstOrDefault(c => c.Id == toSelect.Value);
        }

        // Keep other bindings in sync.
        OnPropertyChanged(nameof(ConditionDefinitions));
    }

    private static bool TryNormalizeHexColor(string? hex, out string normalized)
    {
        normalized = "#FFFFFFFF";
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        try
        {
            var c = Color.Parse(hex.Trim());
            normalized = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateSelectedRgbFromHex(string? hex)
    {
        try
        {
            var c = Color.Parse(hex ?? "#FFFFFFFF");
            SelectedColorRText = c.R.ToString();
            SelectedColorGText = c.G.ToString();
            SelectedColorBText = c.B.ToString();
        }
        catch
        {
            SelectedColorRText = "255";
            SelectedColorGText = "255";
            SelectedColorBText = "255";
        }
    }

    private void UpdateNewCustomRgbFromHex(string? hex)
    {
        try
        {
            var c = Color.Parse(hex ?? "#FFFFFFFF");
            NewCustomColorRText = c.R.ToString();
            NewCustomColorGText = c.G.ToString();
            NewCustomColorBText = c.B.ToString();
        }
        catch
        {
            NewCustomColorRText = "255";
            NewCustomColorGText = "255";
            NewCustomColorBText = "255";
        }
    }

    private static bool TryBuildHexFromRgb(string? rText, string? gText, string? bText, out string hex)
    {
        hex = "#FFFFFFFF";
        if (!byte.TryParse(rText, out var r) || !byte.TryParse(gText, out var g) || !byte.TryParse(bText, out var b))
        {
            return false;
        }

        hex = $"#FF{r:X2}{g:X2}{b:X2}";
        return true;
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

            var vm = new InitiativeEntryViewModel(id)
            {
                Name = entry.Name ?? string.Empty,
                Initiative = entry.Initiative ?? string.Empty,
                Mod = entry.Mod ?? string.Empty,
                IsHidden = entry.IsHidden,
                Notes = entry.Notes,
                MaxHp = entry.MaxHp ?? string.Empty,
                CurrentHp = entry.CurrentHp ?? string.Empty,
            };

            // Back-compat: if MaxHp exists but CurrentHp is missing, keep CurrentHp blank.
            if (!string.IsNullOrEmpty(vm.MaxHp) && entry.CurrentHp is null)
            {
                vm.CurrentHp = string.Empty;
            }

            var seenConditionIds = new HashSet<Guid>();
            foreach (var c in entry.Conditions ?? new List<InitiativeTrackerConfigEntryCondition>())
            {
                if (c.ConditionId == Guid.Empty || !seenConditionIds.Add(c.ConditionId))
                {
                    continue;
                }

                if (!ConditionLibrary.TryGet(c.ConditionId, out var def))
                {
                    continue;
                }

                var rounds = def.IsManualOnly ? null : NormalizeRoundsOrNull(c.RoundsRemaining);
                vm.Conditions.Add(new InitiativeEntryConditionViewModel(
                    owner: vm,
                    conditionId: def.Id,
                    shortTag: def.Name,
                    colorHex: def.ColorHex,
                    isManualOnly: def.IsManualOnly,
                    roundsRemaining: rounds));
            }

            Entries.Add(vm);
        }

        if (Entries.Count == 0)
        {
            for (var i = 0; i < DefaultRowCount; i++)
            {
                Entries.Add(CreateBlankEntry());
            }
        }

        const int round = 1;
        _suppressRoundSync = true;
        try
        {
            Round = round;
        }
        finally
        {
            _suppressRoundSync = false;
        }

        // Always start at the top with no active selection.
        RebuildStateFromEntries(sort: false, activeIdOverride: null);
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
                UnhookEntry(o);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var n in e.NewItems.OfType<InitiativeEntryViewModel>())
            {
                HookEntry(n);
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
    private void RollInitiative(InitiativeEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        var mod = ParseIntOrZero(entry.Mod);
        var roll = Random.Shared.Next(1, 21);
        entry.Initiative = (mod + roll).ToString();
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

        // End-of-turn tick-down: decrement the currently-active eligible entry when advancing forward.
        // If the current active entry is ineligible/missing, skip tick-down per spec.
        if (forward && currentIdx >= 0)
        {
            var endedTurnId = eligibleIds[currentIdx];
            if (vmById.TryGetValue(endedTurnId, out var endedVm))
            {
                _suppressEntrySync = true;
                try
                {
                    TickDownTimedConditions(endedVm);
                }
                finally
                {
                    _suppressEntrySync = false;
                }
            }
        }

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

        // Update Round without invoking OnRoundChanged (RebuildStateFromEntries will use this).
        _suppressRoundSync = true;
        try
        {
            Round = newRound;
        }
        finally
        {
            _suppressRoundSync = false;
        }

        RebuildStateFromEntries(sort: false, activeIdOverride: newActive);
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
        var entries = Entries.Select(vm => new InitiativeEntry(
                Id: vm.Id,
                Name: vm.Name,
                Initiative: ParseIntOrZero(vm.Initiative),
                Mod: ParseIntOrZero(vm.Mod),
                IsHidden: vm.IsHidden,
                Notes: vm.Notes,
                MaxHp: vm.MaxHp ?? string.Empty,
                CurrentHp: vm.CurrentHp ?? string.Empty,
                Conditions: vm.Conditions
                    .Select(c => new AppliedCondition(c.ConditionId, c.RoundsRemaining))
                    .ToArray()))
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

    private static int ParseIntOrZero(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return int.TryParse(text, out var value) ? value : 0;
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
            MaxHp = string.Empty,
            CurrentHp = string.Empty,
        };
    }

    [RelayCommand]
    private void AddConditionToEntry(InitiativeEntryViewModel? entry)
    {
        if (entry is null || entry.SelectedConditionToAdd is null)
        {
            return;
        }

        var def = entry.SelectedConditionToAdd;
        var rounds = def.IsManualOnly ? null : NormalizeRoundsOrNull(entry.SelectedConditionRoundsToAdd);

        // Re-adding replaces.
        var existing = entry.Conditions.FirstOrDefault(c => c.ConditionId == def.Id);
        if (existing is not null)
        {
            existing.ShortTag = def.Name;
            existing.ColorHex = def.ColorHex;
            existing.RoundsRemaining = rounds;
        }
        else
        {
            entry.Conditions.Add(new InitiativeEntryConditionViewModel(
                owner: entry,
                conditionId: def.Id,
                shortTag: def.Name,
                colorHex: def.ColorHex,
                isManualOnly: def.IsManualOnly,
                roundsRemaining: rounds));
        }

        RebuildStateFromEntries(sort: false);
    }

    [RelayCommand]
    private void RemoveConditionFromEntry(InitiativeEntryConditionViewModel? condition)
    {
        if (condition is null)
        {
            return;
        }

        condition.Owner.Conditions.Remove(condition);
        RebuildStateFromEntries(sort: false);
    }

    private void HookEntry(InitiativeEntryViewModel entry)
    {
        entry.PropertyChanged += OnEntryPropertyChanged;
        entry.Conditions.CollectionChanged += OnEntryConditionsCollectionChanged;
        foreach (var c in entry.Conditions)
        {
            c.PropertyChanged += OnEntryConditionPropertyChanged;
        }
    }

    private void UnhookEntry(InitiativeEntryViewModel entry)
    {
        entry.PropertyChanged -= OnEntryPropertyChanged;
        entry.Conditions.CollectionChanged -= OnEntryConditionsCollectionChanged;
        foreach (var c in entry.Conditions)
        {
            c.PropertyChanged -= OnEntryConditionPropertyChanged;
        }
    }

    private void OnEntryConditionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressEntrySync)
        {
            return;
        }

        if (e.OldItems is not null)
        {
            foreach (var c in e.OldItems.OfType<InitiativeEntryConditionViewModel>())
            {
                c.PropertyChanged -= OnEntryConditionPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var c in e.NewItems.OfType<InitiativeEntryConditionViewModel>())
            {
                c.PropertyChanged += OnEntryConditionPropertyChanged;
            }
        }

        RebuildStateFromEntries(sort: false);
    }

    private void OnEntryConditionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressEntrySync)
        {
            return;
        }

        RebuildStateFromEntries(sort: false);
    }

    private static void TickDownTimedConditions(InitiativeEntryViewModel entry)
    {
        for (var i = entry.Conditions.Count - 1; i >= 0; i--)
        {
            var c = entry.Conditions[i];
            if (c.IsManualOnly || !c.RoundsRemaining.HasValue)
            {
                continue;
            }

            var next = c.RoundsRemaining.Value - 1;
            if (next <= 0)
            {
                entry.Conditions.RemoveAt(i);
            }
            else
            {
                c.RoundsRemaining = next;
            }
        }
    }

    private void RefreshAllEntryConditionAppearance()
    {
        foreach (var entry in Entries)
        {
            foreach (var c in entry.Conditions)
            {
                if (!ConditionLibrary.TryGet(c.ConditionId, out var def))
                {
                    continue;
                }

                c.ShortTag = def.Name;
                c.ColorHex = def.ColorHex;
            }
        }
    }

    private void PurgeConditionFromAllEntries(Guid conditionId)
    {
        foreach (var entry in Entries)
        {
            for (var i = entry.Conditions.Count - 1; i >= 0; i--)
            {
                if (entry.Conditions[i].ConditionId == conditionId)
                {
                    entry.Conditions.RemoveAt(i);
                }
            }
        }
    }

    private static int NormalizeRounds(int rounds)
    {
        return Math.Clamp(rounds, 1, 10);
    }

    private static int? NormalizeRoundsOrNull(int? rounds)
    {
        if (!rounds.HasValue)
        {
            return null;
        }

        return NormalizeRounds(rounds.Value);
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
        OnPropertyChanged(nameof(PrevTurnDetailsText));
        OnPropertyChanged(nameof(UpTurnDetailsText));
        OnPropertyChanged(nameof(NextTurnDetailsText));
        OnPropertyChanged(nameof(UpTurnLineText));
        OnPropertyChanged(nameof(NextTurnLineText));
        StateChanged?.Invoke();
    }

    private string BuildPrevTurnPreviewDetails()
    {
        var eligible = GetEligibleIdsInStateOrder();
        if (eligible.Length == 0)
        {
            return "—";
        }

        var idx = -1;
        if (_state.ActiveId.HasValue)
        {
            for (var i = 0; i < eligible.Length; i++)
            {
                if (eligible[i] == _state.ActiveId.Value)
                {
                    idx = i;
                    break;
                }
            }
        }

        var targetIdx = idx >= 0 ? (idx - 1 + eligible.Length) % eligible.Length : eligible.Length - 1;
        var id = eligible[targetIdx];

        var vm = Entries.FirstOrDefault(e => e.Id == id);
        if (vm is null)
        {
            return "—";
        }

        var name = (vm.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            name = UnnamedToken;
        }

        var notes = (vm.Notes ?? string.Empty).Trim();
        return notes.Length > 0 ? $"{name} ({notes})" : name;
    }

    private string BuildTurnPreviewDetails(int offsetFromUp)
    {
        var eligible = GetEligibleIdsInStateOrder();
        if (eligible.Length == 0)
        {
            return "—";
        }

        var idx = -1;
        if (_state.ActiveId.HasValue)
        {
            for (var i = 0; i < eligible.Length; i++)
            {
                if (eligible[i] == _state.ActiveId.Value)
                {
                    idx = i;
                    break;
                }
            }
        }

        var upIdx = idx >= 0 ? idx : 0;
        var targetIdx = idx >= 0
            ? (upIdx + offsetFromUp) % eligible.Length
            : Math.Min(upIdx + offsetFromUp, eligible.Length - 1);

        var id = eligible[targetIdx];
        var vm = Entries.FirstOrDefault(e => e.Id == id);
        if (vm is null)
        {
            return "—";
        }

        var name = (vm.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            name = UnnamedToken;
        }

        var notes = (vm.Notes ?? string.Empty).Trim();
        return notes.Length > 0 ? $"{name} ({notes})" : name;
    }

    private Guid[] GetEligibleIdsInStateOrder()
    {
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

            return !string.IsNullOrWhiteSpace(vm.Initiative);
        }

        return _state.Entries
            .Select(e => e.Id)
            .Where(id => vmById.TryGetValue(id, out var vm) && IsEligible(vm))
            .ToArray();
    }
}
