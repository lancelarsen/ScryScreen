namespace ScryScreen.App.Models;

public readonly record struct HourglassPhysicsSettings(
    int ParticleCount,
    double Gravity,
    double Density,
    double ParticleSize,
    int MaxReleasePerFrame)
{
    public static readonly HourglassPhysicsSettings Default = new(
        ParticleCount: 1000,
        // Grid-sand simulation values (units are in "cells" and pixels)
        Gravity: 90.0,
        Density: 5.0,
        ParticleSize: 6.0,
        MaxReleasePerFrame: 6);
}
