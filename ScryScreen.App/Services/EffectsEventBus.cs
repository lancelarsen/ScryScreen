using System;

namespace ScryScreen.App.Services;

internal static class EffectsEventBus
{
    public static event Action<int, double>? LightningFlash;
    public static event Action<int, double>? QuakeStarted;
    public static event Action<int>? QuakeEnded;

    public static void RaiseLightningFlash(int portalNumber, double intensity)
    {
        try { LightningFlash?.Invoke(portalNumber, intensity); } catch { }
    }

    public static void RaiseQuakeStarted(int portalNumber, double intensity)
    {
        try { QuakeStarted?.Invoke(portalNumber, intensity); } catch { }
    }

    public static void RaiseQuakeEnded(int portalNumber)
    {
        try { QuakeEnded?.Invoke(portalNumber); } catch { }
    }
}
