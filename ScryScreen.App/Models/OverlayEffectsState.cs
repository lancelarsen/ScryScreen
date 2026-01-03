namespace ScryScreen.App.Models;

public sealed record OverlayEffectsState(
    bool RainEnabled,
    double RainIntensity,
    bool SnowEnabled,
    double SnowIntensity,
    bool AshEnabled,
    double AshIntensity,
    bool SandEnabled,
    double SandIntensity,
    bool FogEnabled,
    double FogIntensity,
    bool SmokeEnabled,
    double SmokeIntensity,
    bool LightningEnabled,
    double LightningIntensity,
    bool QuakeEnabled,
    double QuakeIntensity,
    long LightningTrigger,
    long QuakeTrigger)
{
    public static readonly OverlayEffectsState None = new(
        RainEnabled: false,
        RainIntensity: 0,
        SnowEnabled: false,
        SnowIntensity: 0,
        AshEnabled: false,
        AshIntensity: 0,
        SandEnabled: false,
        SandIntensity: 0,
        FogEnabled: false,
        FogIntensity: 0,
        SmokeEnabled: false,
        SmokeIntensity: 0,
        LightningEnabled: false,
        LightningIntensity: 0,
        QuakeEnabled: false,
        QuakeIntensity: 0,
        LightningTrigger: 0,
        QuakeTrigger: 0);
}
