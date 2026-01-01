using System;

namespace ScryScreen.Core.InitiativeTracker;

public sealed record InitiativeEntry(
    Guid Id,
    string Name,
    int Initiative,
    bool IsHidden = false,
    string? Notes = null)
{
    public static InitiativeEntry Create(string name, int initiative, bool isHidden = false, string? notes = null)
    {
        return new InitiativeEntry(
            Id: Guid.NewGuid(),
            Name: name,
            Initiative: initiative,
            IsHidden: isHidden,
            Notes: notes);
    }
}
