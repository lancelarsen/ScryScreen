using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ScryScreen.App.Models;

namespace ScryScreen.App.ViewModels;

public sealed partial class EffectsSettingsViewModel : ViewModelBase
{
    private bool _normalizingEffectRanges;
    private bool _normalizingEffectsVolume;

    // Overlay effects (global). Multiple can be enabled at once.
    [ObservableProperty]
    private bool rainEnabled;

    // Global multiplier for effect audio (0..2 recommended).
    [ObservableProperty]
    private double effectsVolume = 1.0;

    [ObservableProperty]
    private bool rainSoundEnabled;

    [ObservableProperty]
    private double rainMin = 0.1;

    [ObservableProperty]
    private double rainMax = 5;

    [ObservableProperty]
    private double rainIntensity = 0.5;

    [ObservableProperty]
    private bool snowEnabled;

    [ObservableProperty]
    private bool snowSoundEnabled;

    [ObservableProperty]
    private double snowMin = 0.1;

    [ObservableProperty]
    private double snowMax = 4;

    [ObservableProperty]
    private double snowIntensity = 0.5;

    [ObservableProperty]
    private bool ashEnabled;

    [ObservableProperty]
    private bool ashSoundEnabled;

    [ObservableProperty]
    private double ashMin = 0.1;

    [ObservableProperty]
    private double ashMax = 4;

    [ObservableProperty]
    private double ashIntensity = 0.5;

    [ObservableProperty]
    private bool fireEnabled;

    [ObservableProperty]
    private bool fireSoundEnabled;

    [ObservableProperty]
    private double fireMin = 1;

    [ObservableProperty]
    private double fireMax = 5;

    [ObservableProperty]
    private double fireIntensity = 1;

    [ObservableProperty]
    private bool sandEnabled;

    [ObservableProperty]
    private bool sandSoundEnabled;

    [ObservableProperty]
    private double sandMin = 0.1;

    [ObservableProperty]
    private double sandMax = 5;

    [ObservableProperty]
    private double sandIntensity = 0.5;

    [ObservableProperty]
    private bool fogEnabled;

    [ObservableProperty]
    private bool fogSoundEnabled;

    [ObservableProperty]
    private double fogMin = 0.5;

    [ObservableProperty]
    private double fogMax = 4;

    [ObservableProperty]
    private double fogIntensity = 1;

    [ObservableProperty]
    private bool smokeEnabled;

    [ObservableProperty]
    private bool smokeSoundEnabled;

    [ObservableProperty]
    private double smokeMin = 0.5;

    [ObservableProperty]
    private double smokeMax = 2;

    [ObservableProperty]
    private double smokeIntensity = 0.5;

    [ObservableProperty]
    private bool lightningEnabled;

    [ObservableProperty]
    private bool lightningSoundEnabled;

    [ObservableProperty]
    private double lightningMin = 0.1;

    [ObservableProperty]
    private double lightningMax = 5;

    [ObservableProperty]
    private double lightningIntensity = 0.35;

    [ObservableProperty]
    private bool quakeEnabled;

    [ObservableProperty]
    private bool quakeSoundEnabled;

    [ObservableProperty]
    private double quakeMin = 0.1;

    [ObservableProperty]
    private double quakeMax = 5;

    [ObservableProperty]
    private double quakeIntensity = 0.35;

    partial void OnEffectsVolumeChanged(double value)
    {
        if (_normalizingEffectsVolume)
        {
            return;
        }

        _normalizingEffectsVolume = true;
        try
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                EffectsVolume = 1.0;
                return;
            }

            if (value < 0)
            {
                EffectsVolume = 0;
                return;
            }

            if (value > 2)
            {
                EffectsVolume = 2;
            }
        }
        finally
        {
            _normalizingEffectsVolume = false;
        }
    }

    public EffectsConfig ExportEffectsConfig()
        => new()
        {
            EffectsVolume = EffectsVolume,

            RainEnabled = RainEnabled,
            RainSoundEnabled = RainSoundEnabled,
            RainMin = RainMin,
            RainMax = RainMax,
            RainIntensity = RainIntensity,

            SnowEnabled = SnowEnabled,
            SnowSoundEnabled = SnowSoundEnabled,
            SnowMin = SnowMin,
            SnowMax = SnowMax,
            SnowIntensity = SnowIntensity,

            AshEnabled = AshEnabled,
            AshSoundEnabled = AshSoundEnabled,
            AshMin = AshMin,
            AshMax = AshMax,
            AshIntensity = AshIntensity,

            FireEnabled = FireEnabled,
            FireSoundEnabled = FireSoundEnabled,
            FireMin = FireMin,
            FireMax = FireMax,
            FireIntensity = FireIntensity,

            SandEnabled = SandEnabled,
            SandSoundEnabled = SandSoundEnabled,
            SandMin = SandMin,
            SandMax = SandMax,
            SandIntensity = SandIntensity,

            FogEnabled = FogEnabled,
            FogSoundEnabled = FogSoundEnabled,
            FogMin = FogMin,
            FogMax = FogMax,
            FogIntensity = FogIntensity,

            SmokeEnabled = SmokeEnabled,
            SmokeSoundEnabled = SmokeSoundEnabled,
            SmokeMin = SmokeMin,
            SmokeMax = SmokeMax,
            SmokeIntensity = SmokeIntensity,

            LightningEnabled = LightningEnabled,
            LightningSoundEnabled = LightningSoundEnabled,
            LightningMin = LightningMin,
            LightningMax = LightningMax,
            LightningIntensity = LightningIntensity,

            QuakeEnabled = QuakeEnabled,
            QuakeSoundEnabled = QuakeSoundEnabled,
            QuakeMin = QuakeMin,
            QuakeMax = QuakeMax,
            QuakeIntensity = QuakeIntensity,
        };

    public EffectsConfig ExportEffectsConfigForPersistence()
    {
        var config = ExportEffectsConfig();

        // Per user preference: enabled/sound toggles are not persisted.
        // Always start effects + audio OFF.
        config.RainEnabled = false;
        config.RainSoundEnabled = false;
        config.SnowEnabled = false;
        config.SnowSoundEnabled = false;
        config.AshEnabled = false;
        config.AshSoundEnabled = false;
        config.FireEnabled = false;
        config.FireSoundEnabled = false;
        config.SandEnabled = false;
        config.SandSoundEnabled = false;
        config.FogEnabled = false;
        config.FogSoundEnabled = false;
        config.SmokeEnabled = false;
        config.SmokeSoundEnabled = false;
        config.LightningEnabled = false;
        config.LightningSoundEnabled = false;
        config.QuakeEnabled = false;
        config.QuakeSoundEnabled = false;

        return config;
    }

    public void ImportEffectsConfig(EffectsConfig config)
    {
        if (config is null)
        {
            return;
        }

        EffectsVolume = config.EffectsVolume;

        RainEnabled = config.RainEnabled;
        RainSoundEnabled = config.RainSoundEnabled;
        RainMin = config.RainMin;
        RainMax = config.RainMax;
        RainIntensity = config.RainIntensity;

        SnowEnabled = config.SnowEnabled;
        SnowSoundEnabled = config.SnowSoundEnabled;
        SnowMin = config.SnowMin;
        SnowMax = config.SnowMax;
        SnowIntensity = config.SnowIntensity;

        AshEnabled = config.AshEnabled;
        AshSoundEnabled = config.AshSoundEnabled;
        AshMin = config.AshMin;
        AshMax = config.AshMax;
        AshIntensity = config.AshIntensity;

        FireEnabled = config.FireEnabled;
        FireSoundEnabled = config.FireSoundEnabled;
        FireMin = config.FireMin;
        FireMax = config.FireMax;
        FireIntensity = config.FireIntensity;

        SandEnabled = config.SandEnabled;
        SandSoundEnabled = config.SandSoundEnabled;
        SandMin = config.SandMin;
        SandMax = config.SandMax;
        SandIntensity = config.SandIntensity;

        FogEnabled = config.FogEnabled;
        FogSoundEnabled = config.FogSoundEnabled;
        FogMin = config.FogMin;
        FogMax = config.FogMax;
        FogIntensity = config.FogIntensity;

        SmokeEnabled = config.SmokeEnabled;
        SmokeSoundEnabled = config.SmokeSoundEnabled;
        SmokeMin = config.SmokeMin;
        SmokeMax = config.SmokeMax;
        SmokeIntensity = config.SmokeIntensity;

        LightningEnabled = config.LightningEnabled;
        LightningSoundEnabled = config.LightningSoundEnabled;
        LightningMin = config.LightningMin;
        LightningMax = config.LightningMax;
        LightningIntensity = config.LightningIntensity;

        QuakeEnabled = config.QuakeEnabled;
        QuakeSoundEnabled = config.QuakeSoundEnabled;
        QuakeMin = config.QuakeMin;
        QuakeMax = config.QuakeMax;
        QuakeIntensity = config.QuakeIntensity;
    }

    public void ImportEffectsConfigForPersistence(EffectsConfig config)
    {
        if (config is null)
        {
            return;
        }

        // Apply numeric/range values...
        EffectsVolume = config.EffectsVolume;

        RainMin = config.RainMin;
        RainMax = config.RainMax;
        RainIntensity = config.RainIntensity;

        SnowMin = config.SnowMin;
        SnowMax = config.SnowMax;
        SnowIntensity = config.SnowIntensity;

        AshMin = config.AshMin;
        AshMax = config.AshMax;
        AshIntensity = config.AshIntensity;

        FireMin = config.FireMin;
        FireMax = config.FireMax;
        FireIntensity = config.FireIntensity;

        SandMin = config.SandMin;
        SandMax = config.SandMax;
        SandIntensity = config.SandIntensity;

        FogMin = config.FogMin;
        FogMax = config.FogMax;
        FogIntensity = config.FogIntensity;

        SmokeMin = config.SmokeMin;
        SmokeMax = config.SmokeMax;
        SmokeIntensity = config.SmokeIntensity;

        LightningMin = config.LightningMin;
        LightningMax = config.LightningMax;
        LightningIntensity = config.LightningIntensity;

        QuakeMin = config.QuakeMin;
        QuakeMax = config.QuakeMax;
        QuakeIntensity = config.QuakeIntensity;

        // ...but do NOT restore enabled/sound flags.
        RainEnabled = false;
        RainSoundEnabled = false;
        SnowEnabled = false;
        SnowSoundEnabled = false;
        AshEnabled = false;
        AshSoundEnabled = false;
        FireEnabled = false;
        FireSoundEnabled = false;
        SandEnabled = false;
        SandSoundEnabled = false;
        FogEnabled = false;
        FogSoundEnabled = false;
        SmokeEnabled = false;
        SmokeSoundEnabled = false;
        LightningEnabled = false;
        LightningSoundEnabled = false;
        QuakeEnabled = false;
        QuakeSoundEnabled = false;
    }

    private static double SanitizeNonNegative(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
        {
            return 0;
        }

        return v < 0 ? 0 : v;
    }

    private void NormalizeRange(
        Func<double> getMin,
        Action<double> setMin,
        Func<double> getMax,
        Action<double> setMax,
        Func<double> getValue,
        Action<double> setValue)
    {
        if (_normalizingEffectRanges)
        {
            return;
        }

        _normalizingEffectRanges = true;
        try
        {
            var min = SanitizeNonNegative(getMin());
            var max = SanitizeNonNegative(getMax());

            if (max < min)
            {
                max = min;
            }

            if (min != getMin())
            {
                setMin(min);
            }

            if (max != getMax())
            {
                setMax(max);
            }

            var v = getValue();
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                v = min;
            }

            if (v < min)
            {
                setValue(min);
            }
            else if (v > max)
            {
                setValue(max);
            }
        }
        finally
        {
            _normalizingEffectRanges = false;
        }
    }

    partial void OnRainMinChanged(double value) => NormalizeRange(() => RainMin, v => RainMin = v, () => RainMax, v => RainMax = v, () => RainIntensity, v => RainIntensity = v);
    partial void OnRainMaxChanged(double value) => NormalizeRange(() => RainMin, v => RainMin = v, () => RainMax, v => RainMax = v, () => RainIntensity, v => RainIntensity = v);

    partial void OnSnowMinChanged(double value) => NormalizeRange(() => SnowMin, v => SnowMin = v, () => SnowMax, v => SnowMax = v, () => SnowIntensity, v => SnowIntensity = v);
    partial void OnSnowMaxChanged(double value) => NormalizeRange(() => SnowMin, v => SnowMin = v, () => SnowMax, v => SnowMax = v, () => SnowIntensity, v => SnowIntensity = v);

    partial void OnAshMinChanged(double value) => NormalizeRange(() => AshMin, v => AshMin = v, () => AshMax, v => AshMax = v, () => AshIntensity, v => AshIntensity = v);
    partial void OnAshMaxChanged(double value) => NormalizeRange(() => AshMin, v => AshMin = v, () => AshMax, v => AshMax = v, () => AshIntensity, v => AshIntensity = v);

    partial void OnFireMinChanged(double value) => NormalizeRange(() => FireMin, v => FireMin = v, () => FireMax, v => FireMax = v, () => FireIntensity, v => FireIntensity = v);
    partial void OnFireMaxChanged(double value) => NormalizeRange(() => FireMin, v => FireMin = v, () => FireMax, v => FireMax = v, () => FireIntensity, v => FireIntensity = v);

    partial void OnSandMinChanged(double value) => NormalizeRange(() => SandMin, v => SandMin = v, () => SandMax, v => SandMax = v, () => SandIntensity, v => SandIntensity = v);
    partial void OnSandMaxChanged(double value) => NormalizeRange(() => SandMin, v => SandMin = v, () => SandMax, v => SandMax = v, () => SandIntensity, v => SandIntensity = v);

    partial void OnFogMinChanged(double value) => NormalizeRange(() => FogMin, v => FogMin = v, () => FogMax, v => FogMax = v, () => FogIntensity, v => FogIntensity = v);
    partial void OnFogMaxChanged(double value) => NormalizeRange(() => FogMin, v => FogMin = v, () => FogMax, v => FogMax = v, () => FogIntensity, v => FogIntensity = v);

    partial void OnSmokeMinChanged(double value) => NormalizeRange(() => SmokeMin, v => SmokeMin = v, () => SmokeMax, v => SmokeMax = v, () => SmokeIntensity, v => SmokeIntensity = v);
    partial void OnSmokeMaxChanged(double value) => NormalizeRange(() => SmokeMin, v => SmokeMin = v, () => SmokeMax, v => SmokeMax = v, () => SmokeIntensity, v => SmokeIntensity = v);

    partial void OnLightningMinChanged(double value) => NormalizeRange(() => LightningMin, v => LightningMin = v, () => LightningMax, v => LightningMax = v, () => LightningIntensity, v => LightningIntensity = v);
    partial void OnLightningMaxChanged(double value) => NormalizeRange(() => LightningMin, v => LightningMin = v, () => LightningMax, v => LightningMax = v, () => LightningIntensity, v => LightningIntensity = v);

    partial void OnQuakeMinChanged(double value) => NormalizeRange(() => QuakeMin, v => QuakeMin = v, () => QuakeMax, v => QuakeMax = v, () => QuakeIntensity, v => QuakeIntensity = v);
    partial void OnQuakeMaxChanged(double value) => NormalizeRange(() => QuakeMin, v => QuakeMin = v, () => QuakeMax, v => QuakeMax = v, () => QuakeIntensity, v => QuakeIntensity = v);
}
