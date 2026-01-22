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

public sealed record DiceRollDiceTerm(int Sides, int Count, int Sign = 1);

public sealed record DiceRollRequest(long RequestId, IReadOnlyList<DiceRollDiceTerm> Terms, DiceRollDirection Direction = DiceRollDirection.Right);

public sealed record DiceRollerState(
    string Text,
    long RollId,
    IReadOnlyList<DiceDieRotation> Rotations,
    IReadOnlyList<DiceDieVisualConfig> VisualConfigs,
    long VisualConfigRevision = 0,
    DiceRollDirection RollDirection = DiceRollDirection.Right,
    IReadOnlyList<DiceRollRequest>? RollRequests = null,
    long ClearDiceId = 0,
    bool DebugVisible = false)
{
    public static DiceRollerState Default { get; } = new(
        Text: string.Empty,
        RollId: 0,
        Rotations: System.Array.Empty<DiceDieRotation>(),
        VisualConfigs: System.Array.Empty<DiceDieVisualConfig>(),
        VisualConfigRevision: 0,
        RollRequests: System.Array.Empty<DiceRollRequest>(),
        DebugVisible: false);
}
