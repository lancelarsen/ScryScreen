namespace ScryScreen.App.Models;

public sealed class EffectsConfig
{
    public int Version { get; set; } = 1;

    // Global multiplier for all effect sounds (0..2 recommended).
    public double EffectsVolume { get; set; } = 1.0;

    public bool RainEnabled { get; set; }
    public bool RainSoundEnabled { get; set; }
    public double RainMin { get; set; } = 0.1;
    public double RainMax { get; set; } = 5;
    public double RainIntensity { get; set; } = 0.5;

    public bool SnowEnabled { get; set; }
    public bool SnowSoundEnabled { get; set; }
    public double SnowMin { get; set; } = 0.1;
    public double SnowMax { get; set; } = 4;
    public double SnowIntensity { get; set; } = 0.5;

    public bool AshEnabled { get; set; }
    public bool AshSoundEnabled { get; set; }
    public double AshMin { get; set; } = 0.1;
    public double AshMax { get; set; } = 4;
    public double AshIntensity { get; set; } = 0.5;

    public bool FireEnabled { get; set; }
    public bool FireSoundEnabled { get; set; }
    public double FireMin { get; set; } = 1;
    public double FireMax { get; set; } = 5;
    public double FireIntensity { get; set; } = 1;

    public bool SandEnabled { get; set; }
    public bool SandSoundEnabled { get; set; }
    public double SandMin { get; set; } = 0.1;
    public double SandMax { get; set; } = 5;
    public double SandIntensity { get; set; } = 0.5;

    public bool FogEnabled { get; set; }
    public bool FogSoundEnabled { get; set; }
    public double FogMin { get; set; } = 0.5;
    public double FogMax { get; set; } = 4;
    public double FogIntensity { get; set; } = 1;

    public bool SmokeEnabled { get; set; }
    public bool SmokeSoundEnabled { get; set; }
    public double SmokeMin { get; set; } = 0.5;
    public double SmokeMax { get; set; } = 2;
    public double SmokeIntensity { get; set; } = 0.5;

    public bool LightningEnabled { get; set; }
    public bool LightningSoundEnabled { get; set; }
    public double LightningMin { get; set; } = 0.1;
    public double LightningMax { get; set; } = 5;
    public double LightningIntensity { get; set; } = 0.35;

    public bool QuakeEnabled { get; set; }
    public bool QuakeSoundEnabled { get; set; }
    public double QuakeMin { get; set; } = 0.1;
    public double QuakeMax { get; set; } = 5;
    public double QuakeIntensity { get; set; } = 0.35;
}
