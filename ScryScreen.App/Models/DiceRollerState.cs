using System.Collections.Generic;

namespace ScryScreen.App.Models;

public sealed record DiceDieRotation(int Sides, float X, float Y, float Z, float W);

public sealed record DiceRollRequest(long RequestId, int Sides);

public sealed record DiceRollerState(
    string Text,
    double OverlayOpacity,
    long RollId,
    IReadOnlyList<DiceDieRotation> Rotations,
    DiceRollRequest? RollRequest = null,
    long ClearDiceId = 0)
{
    public static DiceRollerState Default { get; } = new(Text: string.Empty, OverlayOpacity: 0.85, RollId: 0, Rotations: System.Array.Empty<DiceDieRotation>());
}
