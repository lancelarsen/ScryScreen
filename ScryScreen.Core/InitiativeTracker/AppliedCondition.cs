using System;

namespace ScryScreen.Core.InitiativeTracker;

public sealed record AppliedCondition(
    Guid ConditionId,
    int? RoundsRemaining)
{
    public AppliedCondition Normalize()
    {
        if (RoundsRemaining is null)
        {
            return this;
        }

        if (RoundsRemaining.Value < 1)
        {
            return this with { RoundsRemaining = 1 };
        }

        return this;
    }
}
