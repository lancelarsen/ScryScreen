using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScryScreen.Core.InitiativeTracker;

namespace ScryScreen.App.ViewModels;

public sealed partial class InitiativeTrackerViewModel : ViewModelBase
{
    private InitiativeTrackerState _state = InitiativeTrackerState.Empty;

    public event Action? StateChanged;

    public InitiativeTrackerViewModel()
    {
        Entries.CollectionChanged += OnEntriesCollectionChanged;
    }

    public ObservableCollection<InitiativeEntryViewModel> Entries { get; } = new();

    [ObservableProperty]
    private InitiativeEntryViewModel? selectedEntry;

    [ObservableProperty]
    private string newName = string.Empty;

    [ObservableProperty]
    private int newInitiative;

    [ObservableProperty]
    private bool newIsHidden;

    [ObservableProperty]
    private int round = 1;

    public Guid? ActiveId => _state.ActiveId;

    public InitiativeTrackerState SnapshotState() => _state;

    public string PortalText => InitiativeTrackerFormatter.ToPortalText(_state);

    private void OnEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
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

        RebuildStateFromEntries(sort: false);
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Any change affects portal output.
        RebuildStateFromEntries(sort: false);
    }

    [RelayCommand]
    private void AddEntry()
    {
        var id = Guid.NewGuid();
        var vm = new InitiativeEntryViewModel(id)
        {
            Name = NewName,
            Initiative = NewInitiative,
            IsHidden = NewIsHidden,
        };

        Entries.Add(vm);
        SelectedEntry = vm;

        NewName = string.Empty;
        NewInitiative = 0;
        NewIsHidden = false;

        Sort();
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        var toRemove = SelectedEntry;
        SelectedEntry = null;
        Entries.Remove(toRemove);
    }

    [RelayCommand]
    private void ClearAll()
    {
        Entries.Clear();
        SelectedEntry = null;
        _state = InitiativeTrackerState.Empty;
        Round = _state.Round;
        RaisePortalTextChanged();
    }

    [RelayCommand]
    private void Sort()
    {
        RebuildStateFromEntries(sort: true);

        // Reorder the collection to match sorted state.
        var sortedIds = _state.Entries.Select(e => e.Id).ToArray();
        var current = Entries.ToList();
        Entries.Clear();
        foreach (var id in sortedIds)
        {
            var vm = current.First(x => x.Id == id);
            Entries.Add(vm);
        }

        RaisePortalTextChanged();
    }

    [RelayCommand]
    private void NextTurn()
    {
        _state = InitiativeTrackerEngine.NextTurn(_state);
        Round = _state.Round;
        RaisePortalTextChanged();
    }

    [RelayCommand]
    private void PreviousTurn()
    {
        _state = InitiativeTrackerEngine.PreviousTurn(_state);
        Round = _state.Round;
        RaisePortalTextChanged();
    }

    [RelayCommand]
    private void SetActiveToSelected()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        _state = InitiativeTrackerEngine.SetActive(_state, SelectedEntry.Id);
        RaisePortalTextChanged();
    }

    [RelayCommand]
    private void ResetRound()
    {
        _state = InitiativeTrackerEngine.SetRound(_state, 1);
        Round = _state.Round;
        RaisePortalTextChanged();
    }

    partial void OnRoundChanged(int value)
    {
        // Round is editable; keep engine normalized.
        _state = InitiativeTrackerEngine.SetRound(_state, value);
        RaisePortalTextChanged();
    }

    private void RebuildStateFromEntries(bool sort)
    {
        var entries = Entries.Select(vm => new InitiativeEntry(
                Id: vm.Id,
                Name: vm.Name,
                Initiative: vm.Initiative,
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
        RaisePortalTextChanged();
    }

    private void RaisePortalTextChanged()
    {
        OnPropertyChanged(nameof(PortalText));
        StateChanged?.Invoke();
    }
}
