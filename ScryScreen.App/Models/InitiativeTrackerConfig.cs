using System;
using System.Collections.Generic;

namespace ScryScreen.App.Models;

public sealed class InitiativeTrackerConfig
{
    public int SchemaVersion { get; set; } = 1;

    public int Round { get; set; } = 1;

    public Guid? ActiveId { get; set; }

    public double OverlayOpacity { get; set; }

    // Stored as a string so the config is resilient to enum renames/namespace changes.
    public string PortalFontSize { get; set; } = "Medium";

    public List<InitiativeTrackerConfigEntry> Entries { get; set; } = new();
}

public sealed class InitiativeTrackerConfigEntry
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // Stored as text to preserve "blank vs 0" and any in-progress partial edits.
    public string Initiative { get; set; } = string.Empty;

    public string Mod { get; set; } = string.Empty;

    public bool IsHidden { get; set; }

    public string? Notes { get; set; }
}
