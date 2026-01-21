using System.Collections.Generic;

namespace ScryScreen.App.Models;

public sealed record DiceDieRotation(int Sides, float X, float Y, float Z, float W);

public enum DiceRollDirection
{
    Right,
    Left,
    Up,
    Down,
    Random,
}

public sealed record DiceRollRequest(long RequestId, int Sides, DiceRollDirection Direction = DiceRollDirection.Right);

public sealed record DiceRollerState(
    string Text,
    double OverlayOpacity,
    long RollId,
    IReadOnlyList<DiceDieRotation> Rotations,
    IReadOnlyList<DiceDieVisualConfig> VisualConfigs,
    long VisualConfigRevision = 0,
    DiceRollDirection RollDirection = DiceRollDirection.Right,
    DiceRollRequest? RollRequest = null,
    long ClearDiceId = 0)
{
    public static DiceRollerState Default { get; } = new(
        Text: string.Empty,
        OverlayOpacity: 0.85,
        RollId: 0,
        Rotations: System.Array.Empty<DiceDieRotation>(),
        VisualConfigs: System.Array.Empty<DiceDieVisualConfig>(),
        VisualConfigRevision: 0);
}
