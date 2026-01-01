using System;

namespace ScryScreen.Core.InitiativeTracker;

public sealed record InitiativeEntry(
    Guid Id,
    string Name,
    int Initiative,
    int Mod = 0,
    bool IsHidden = false,
    string? Notes = null)
{
    public static InitiativeEntry Create(string name, int initiative, int mod = 0, bool isHidden = false, string? notes = null)
    {
        return new InitiativeEntry(
            Id: Guid.NewGuid(),
            Name: name,
            Initiative: initiative,
            Mod: mod,
            IsHidden: isHidden,
            Notes: notes);
    }
}
