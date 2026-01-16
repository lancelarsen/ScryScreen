namespace ScryScreen.App.Models;

public sealed record DiceRollerState(string Text, double OverlayOpacity)
{
    public static DiceRollerState Default { get; } = new(Text: string.Empty, OverlayOpacity: 0.85);
}
