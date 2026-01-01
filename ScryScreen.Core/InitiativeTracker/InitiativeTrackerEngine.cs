using System;
using System.Collections.Generic;
using System.Linq;

namespace ScryScreen.Core.InitiativeTracker;

public static class InitiativeTrackerEngine
{
    public static InitiativeTrackerState Add(InitiativeTrackerState state, InitiativeEntry entry)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        if (entry is null) throw new ArgumentNullException(nameof(entry));

        var list = state.Entries.ToList();
        list.Add(Normalize(entry));

        var active = state.ActiveId;
        if (active is null)
        {
            active = entry.Id;
        }

        return state with { Entries = list.ToArray(), ActiveId = active };
    }

    public static InitiativeTrackerState Update(InitiativeTrackerState state, InitiativeEntry entry)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        if (entry is null) throw new ArgumentNullException(nameof(entry));

        var list = state.Entries.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Id == entry.Id)
            {
                list[i] = Normalize(entry);
                return state with { Entries = list.ToArray() };
            }
        }

        return state;
    }

    public static InitiativeTrackerState Remove(InitiativeTrackerState state, Guid id)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        if (state.Entries.Length == 0)
        {
            return state;
        }

        var removedIndex = -1;
        var list = state.Entries.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Id == id)
            {
                removedIndex = i;
                list.RemoveAt(i);
                break;
            }
        }

        if (removedIndex < 0)
        {
            return state;
        }

        if (list.Count == 0)
        {
            return state with { Entries = Array.Empty<InitiativeEntry>(), ActiveId = null };
        }

        // If we removed the active entry, pick the next entry in order, clamping to end.
        var newActive = state.ActiveId;
        if (state.ActiveId == id)
        {
            var nextIndex = Math.Min(removedIndex, list.Count - 1);
            newActive = list[nextIndex].Id;
        }

        return state with { Entries = list.ToArray(), ActiveId = newActive };
    }

    public static InitiativeTrackerState Clear(InitiativeTrackerState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        return InitiativeTrackerState.Empty;
    }

    public static InitiativeTrackerState SetRound(InitiativeTrackerState state, int round)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        if (round < 1) round = 1;
        return state with { Round = round };
    }

    public static InitiativeTrackerState SetActive(InitiativeTrackerState state, Guid id)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        for (var i = 0; i < state.Entries.Length; i++)
        {
            if (state.Entries[i].Id == id)
            {
                return state with { ActiveId = id };
            }
        }

        return state;
    }

    public static InitiativeTrackerState Sort(InitiativeTrackerState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        if (state.Entries.Length <= 1)
        {
            return state;
        }

        var activeId = state.ActiveId;

        // Stable sort: initiative desc, then mod desc, then name, then Id.
        var sorted = state.Entries
            .Select((e, idx) => (Entry: Normalize(e), OriginalIndex: idx))
            .OrderByDescending(x => x.Entry.Initiative)
            .ThenByDescending(x => x.Entry.Mod)
            .ThenBy(x => x.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Entry.Id)
            .ThenBy(x => x.OriginalIndex)
            .Select(x => x.Entry)
            .ToArray();

        // If active is missing, set active to first.
        if (sorted.Length > 0)
        {
            if (activeId is null || sorted.All(e => e.Id != activeId.Value))
            {
                activeId = sorted[0].Id;
            }
        }
        else
        {
            activeId = null;
        }

        return state with { Entries = sorted, ActiveId = activeId };
    }

    public static InitiativeTrackerState NextTurn(InitiativeTrackerState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        if (state.Entries.Length == 0)
        {
            return state;
        }

        var idx = state.GetActiveIndex();
        if (idx < 0)
        {
            return state with { ActiveId = state.Entries[0].Id };
        }

        var next = idx + 1;
        if (next >= state.Entries.Length)
        {
            next = 0;
            return state with { ActiveId = state.Entries[next].Id, Round = state.Round + 1 };
        }

        return state with { ActiveId = state.Entries[next].Id };
    }

    public static InitiativeTrackerState PreviousTurn(InitiativeTrackerState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        if (state.Entries.Length == 0)
        {
            return state;
        }

        var idx = state.GetActiveIndex();
        if (idx < 0)
        {
            return state with { ActiveId = state.Entries[0].Id };
        }

        var prev = idx - 1;
        if (prev < 0)
        {
            prev = state.Entries.Length - 1;
            var round = Math.Max(1, state.Round - 1);
            return state with { ActiveId = state.Entries[prev].Id, Round = round };
        }

        return state with { ActiveId = state.Entries[prev].Id };
    }

    public static InitiativeTrackerState NormalizeState(InitiativeTrackerState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        var entries = state.Entries?.Select(Normalize).ToArray() ?? Array.Empty<InitiativeEntry>();
        var round = state.Round < 1 ? 1 : state.Round;

        Guid? active = state.ActiveId;
        if (entries.Length == 0)
        {
            active = null;
        }
        else if (active is null || entries.All(e => e.Id != active.Value))
        {
            active = entries[0].Id;
        }

        return state with { Entries = entries, Round = round, ActiveId = active };
    }

    private static InitiativeEntry Normalize(InitiativeEntry entry)
    {
        var name = (entry.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            name = "(Unnamed)";
        }

        return entry with { Name = name };
    }
}
