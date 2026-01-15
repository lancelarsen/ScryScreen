using System;
using System.Collections.Generic;

namespace ScryScreen.App.Models;

public sealed class ConditionLibraryConfig
{
    public int SchemaVersion { get; set; } = 1;

    public List<ConditionColorOverride> BuiltInColorOverrides { get; set; } = new();

    public List<CustomConditionConfig> CustomConditions { get; set; } = new();
}

public sealed class ConditionColorOverride
{
    public Guid Id { get; set; }

    public string ColorHex { get; set; } = "#FFFFFFFF";
}

public sealed class CustomConditionConfig
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ShortTag { get; set; } = string.Empty;

    public string ColorHex { get; set; } = "#FFFFFFFF";
}
