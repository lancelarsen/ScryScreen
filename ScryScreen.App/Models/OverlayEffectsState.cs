namespace ScryScreen.App.Models;

public sealed record OverlayEffectsState(
    bool RainEnabled,
    double RainIntensity,
    bool SnowEnabled,
    double SnowIntensity,
    bool FogEnabled,
    double FogIntensity,
    bool SmokeEnabled,
    double SmokeIntensity,
    bool LightningEnabled,
    double LightningIntensity)
{
    public static readonly OverlayEffectsState None = new(
        RainEnabled: false,
        RainIntensity: 0,
        SnowEnabled: false,
        SnowIntensity: 0,
        FogEnabled: false,
        FogIntensity: 0,
        SmokeEnabled: false,
        SmokeIntensity: 0,
        LightningEnabled: false,
        LightningIntensity: 0);
}
