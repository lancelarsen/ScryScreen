using System;
using System.Collections.Generic;

namespace ScryScreen.App.Services;

internal static class DiceRollerEventBus
{
    public static event Action<long, int, int>? SingleDieRollCompleted;

    public static void RaiseSingleDieRollCompleted(long requestId, int sides, int value)
    {
        try { SingleDieRollCompleted?.Invoke(requestId, sides, value); } catch { }
    }
}
