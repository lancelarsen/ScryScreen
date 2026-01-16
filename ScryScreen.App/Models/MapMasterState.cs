namespace ScryScreen.App.Models;

public sealed record MapMasterState(
    double OverlayOpacity,
    double RevealX,
    double RevealY,
    double RevealWidth,
    double RevealHeight)
{
    public static MapMasterState Default { get; } = new(
        OverlayOpacity: 0.85,
        RevealX: 0.25,
        RevealY: 0.25,
        RevealWidth: 0.50,
        RevealHeight: 0.50);
}
