using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScryScreen.Core.InitiativeTracker;

namespace ScryScreen.App.ViewModels;

public sealed partial class InitiativeTrackerViewModel : ViewModelBase
{
    private const int DefaultRowCount = 5;

    private InitiativeTrackerState _state = InitiativeTrackerState.Empty;

    private bool _isReordering;

    private CancellationTokenSource? _resortCts;

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

    public Guid? ActiveId => _state.ActiveId;

    public InitiativeTrackerState SnapshotState() => _state;

    public string PortalText => InitiativeTrackerFormatter.ToPortalText(_state);

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

        // Any change affects portal output.
        var needsResort = e.PropertyName is nameof(InitiativeEntryViewModel.Initiative)
            or nameof(InitiativeEntryViewModel.Mod);

        // Keep portal output updated immediately, but delay sorting/reordering while the user is typing.
        RebuildStateFromEntries(sort: false);

        if (needsResort)
        {
            ScheduleResortAndReorder();
        }
    }

    [RelayCommand]
    private void AddEntry()
    {
        CancelPendingResort();

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

        CancelPendingResort();

        Entries.Remove(entry);
        RebuildStateFromEntries(sort: true);
        ReorderCollectionToMatchState();
    }

    [RelayCommand]
    private void ClearAll()
    {
        CancelPendingResort();

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
        CancelPendingResort();

        RebuildStateFromEntries(sort: true);
        ReorderCollectionToMatchState();
    }

    [RelayCommand]
    private void NextTurn()
    {
        CancelPendingResort();

        _state = InitiativeTrackerEngine.NextTurn(_state);
        Round = _state.Round;
        UpdateActiveFlags();
        RaisePortalTextChanged();
    }

    [RelayCommand]
    private void PreviousTurn()
    {
        CancelPendingResort();

        _state = InitiativeTrackerEngine.PreviousTurn(_state);
        Round = _state.Round;
        UpdateActiveFlags();
        RaisePortalTextChanged();
    }

    [RelayCommand]
    private void ResetRound()
    {
        CancelPendingResort();

        _state = InitiativeTrackerEngine.SetRound(_state, 1);
        Round = _state.Round;
        UpdateActiveFlags();
        RaisePortalTextChanged();
    }

    [RelayCommand]
    private void IncrementRound()
    {
        CancelPendingResort();

        _state = InitiativeTrackerEngine.SetRound(_state, _state.Round + 1);
        Round = _state.Round;
        UpdateActiveFlags();
        RaisePortalTextChanged();
    }

    [RelayCommand]
    private void DecrementRound()
    {
        CancelPendingResort();

        _state = InitiativeTrackerEngine.SetRound(_state, _state.Round - 1);
        Round = _state.Round;
        UpdateActiveFlags();
        RaisePortalTextChanged();
    }

    partial void OnRoundChanged(int value)
    {
        // Round can be changed via commands; normalize if it changes for any other reason.
        _state = InitiativeTrackerEngine.SetRound(_state, value);
        UpdateActiveFlags();
        RaisePortalTextChanged();
    }

    private void ScheduleResortAndReorder()
    {
        CancelPendingResort();

        _resortCts = new CancellationTokenSource();
        var token = _resortCts.Token;

        _ = DebouncedResortAndReorderAsync(token);
    }

    private async Task DebouncedResortAndReorderAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RebuildStateFromEntries(sort: true);
            ReorderCollectionToMatchState();
        });
    }

    private void CancelPendingResort()
    {
        _resortCts?.Cancel();
        _resortCts?.Dispose();
        _resortCts = null;
    }

    private void RebuildStateFromEntries(bool sort)
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
            ActiveId: _state.ActiveId);

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
