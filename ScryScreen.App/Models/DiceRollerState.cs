namespace ScryScreen.App.Models;

public sealed record DiceRollerState(string Text, double OverlayOpacity, long RollId)
{
    public static DiceRollerState Default { get; } = new(Text: string.Empty, OverlayOpacity: 0.85, RollId: 0);
}
