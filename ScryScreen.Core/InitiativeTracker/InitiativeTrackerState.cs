using System;

namespace ScryScreen.Core.InitiativeTracker;

public sealed record InitiativeTrackerState(
    InitiativeEntry[] Entries,
    int Round,
    Guid? ActiveId)
{
    public static InitiativeTrackerState Empty { get; } = new(
        Entries: Array.Empty<InitiativeEntry>(),
        Round: 1,
        ActiveId: null);

    public int GetActiveIndex()
    {
        if (ActiveId is null || Entries.Length == 0)
        {
            return -1;
        }

        for (var i = 0; i < Entries.Length; i++)
        {
            if (Entries[i].Id == ActiveId.Value)
            {
                return i;
            }
        }

        return -1;
    }
}
