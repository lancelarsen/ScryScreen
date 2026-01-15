namespace ScryScreen.App.Models;

public readonly record struct HourglassPhysicsSettings(
    int ParticleCount,
    double Density,
    double ParticleSize,
    int MaxReleasePerFrame)
{
    public static readonly HourglassPhysicsSettings Default = new(
        ParticleCount: 3000,
        // Grid-sand simulation values (units are in "cells" and pixels)
        Density: 5.0,
        ParticleSize: 4.0,
        // Flow is interpreted as grains/sec in the visual (not "per frame").
        MaxReleasePerFrame: 120);
}
