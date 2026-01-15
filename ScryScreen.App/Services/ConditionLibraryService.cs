using System;
using System.Collections.Generic;
using System.Linq;
using ScryScreen.App.Models;

namespace ScryScreen.App.Services;

public sealed class ConditionLibraryService
{
    private readonly Dictionary<Guid, ConditionDefinition> _builtInsById;
    private readonly Dictionary<Guid, ConditionDefinition> _customById;

    private ConditionLibraryService(
        Dictionary<Guid, ConditionDefinition> builtInsById,
        Dictionary<Guid, ConditionDefinition> customById)
    {
        _builtInsById = builtInsById;
        _customById = customById;
    }

    public static ConditionLibraryService LoadOrCreateDefault()
    {
        var defaults = BuildDefaultBuiltIns().ToDictionary(d => d.Id);
        var custom = new Dictionary<Guid, ConditionDefinition>();

        var persisted = ConditionLibraryPersistence.LoadOrDefault();

        // Apply built-in color overrides.
        foreach (var ov in persisted.BuiltInColorOverrides ?? new List<ConditionColorOverride>())
        {
            if (ov.Id == Guid.Empty || string.IsNullOrWhiteSpace(ov.ColorHex))
            {
                continue;
            }

            if (defaults.TryGetValue(ov.Id, out var def))
            {
                defaults[ov.Id] = def with { ColorHex = ov.ColorHex.Trim() };
            }
        }

        // Load custom conditions.
        var seen = new HashSet<Guid>(defaults.Keys);
        foreach (var cc in persisted.CustomConditions ?? new List<CustomConditionConfig>())
        {
            var id = cc.Id;
            if (id == Guid.Empty || seen.Contains(id))
            {
                id = Guid.NewGuid();
            }

            var name = (cc.Name ?? string.Empty).Trim();
            var tag = (cc.ShortTag ?? string.Empty).Trim();
            var color = (cc.ColorHex ?? string.Empty).Trim();

            if (name.Length == 0 || tag.Length == 0 || color.Length == 0)
            {
                continue;
            }

            var def = new ConditionDefinition(
                Id: id,
                Name: name,
                ShortTag: tag,
                ColorHex: color,
                IsBuiltIn: false,
                IsManualOnly: false);

            custom[id] = def;
            seen.Add(id);
        }

        return new ConditionLibraryService(defaults, custom);
    }

    public IReadOnlyList<ConditionDefinition> GetAllDefinitionsAlphabetical()
    {
        return _builtInsById.Values
            .Concat(_customById.Values)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Id)
            .ToArray();
    }

    public bool TryGet(Guid id, out ConditionDefinition definition)
    {
        if (_builtInsById.TryGetValue(id, out definition!))
        {
            return true;
        }

        if (_customById.TryGetValue(id, out definition!))
        {
            return true;
        }

        definition = null!;
        return false;
    }

    public void SetColor(Guid id, string colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return;
        }

        var trimmed = colorHex.Trim();

        if (_builtInsById.TryGetValue(id, out var bi))
        {
            _builtInsById[id] = bi with { ColorHex = trimmed };
            return;
        }

        if (_customById.TryGetValue(id, out var cc))
        {
            _customById[id] = cc with { ColorHex = trimmed };
        }
    }

    public ConditionDefinition AddCustom(string name, string shortTag, string colorHex)
    {
        var n = (name ?? string.Empty).Trim();
        var t = (shortTag ?? string.Empty).Trim();
        var c = (colorHex ?? string.Empty).Trim();

        if (n.Length == 0) throw new ArgumentException("Name is required", nameof(name));
        if (t.Length == 0) throw new ArgumentException("ShortTag is required", nameof(shortTag));
        if (c.Length == 0) throw new ArgumentException("ColorHex is required", nameof(colorHex));

        var id = Guid.NewGuid();
        var def = new ConditionDefinition(
            Id: id,
            Name: n,
            ShortTag: t,
            ColorHex: c,
            IsBuiltIn: false,
            IsManualOnly: false);

        _customById[id] = def;
        return def;
    }

    public bool TryUpdateCustom(Guid id, string name, string shortTag, string colorHex)
    {
        if (!_customById.TryGetValue(id, out var existing))
        {
            return false;
        }

        var n = (name ?? string.Empty).Trim();
        var t = (shortTag ?? string.Empty).Trim();
        var c = (colorHex ?? string.Empty).Trim();

        if (n.Length == 0 || t.Length == 0 || c.Length == 0)
        {
            return false;
        }

        _customById[id] = existing with { Name = n, ShortTag = t, ColorHex = c };
        return true;
    }

    public bool DeleteCustom(Guid id)
        => _customById.Remove(id);

    public void Save()
    {
        // Persist color overrides relative to defaults + custom condition definitions.
        var defaults = BuildDefaultBuiltIns().ToDictionary(d => d.Id);

        var overrides = new List<ConditionColorOverride>();
        foreach (var current in _builtInsById.Values)
        {
            if (defaults.TryGetValue(current.Id, out var def) &&
                !string.Equals(def.ColorHex, current.ColorHex, StringComparison.OrdinalIgnoreCase))
            {
                overrides.Add(new ConditionColorOverride { Id = current.Id, ColorHex = current.ColorHex });
            }
        }

        var customs = _customById.Values
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Id)
            .Select(d => new CustomConditionConfig
            {
                Id = d.Id,
                Name = d.Name,
                ShortTag = d.ShortTag,
                ColorHex = d.ColorHex,
            })
            .ToList();

        var config = new ConditionLibraryConfig
        {
            SchemaVersion = 1,
            BuiltInColorOverrides = overrides,
            CustomConditions = customs,
        };

        ConditionLibraryPersistence.Save(config);
    }

    public static IReadOnlyList<ConditionDefinition> BuildDefaultBuiltIns()
    {
        // Stable IDs for built-ins so encounter files and color overrides remain valid.
        // Colors are just defaults; user can override in Conditions Configuration.
        return new[]
        {
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf01"), "Blinded", "BLIND", "#FFB0B0B0", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf02"), "Bloodied", "BLOOD", "#FFFFA500", true, true),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf03"), "Charmed", "CHARM", "#FFFF4FD8", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf04"), "Dead", "DEAD", "#FF888888", true, true),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf05"), "Deafened", "DEAF", "#FF8FB3FF", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf06"), "Exhaustion (1)", "EXHAUST 1", "#FFD4AF37", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf07"), "Exhaustion (2)", "EXHAUST 2", "#FFD4AF37", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf08"), "Exhaustion (3)", "EXHAUST 3", "#FFD4AF37", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf09"), "Exhaustion (4)", "EXHAUST 4", "#FFD4AF37", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf0a"), "Exhaustion (5)", "EXHAUST 5", "#FFD4AF37", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf0b"), "Exhaustion (6)", "EXHAUST 6", "#FFD4AF37", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf0c"), "Frightened", "FRIGHT", "#FFB388FF", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf0d"), "Grappled", "GRAPPLED", "#FF6EE7B7", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf0e"), "Incapacitated", "INCAP", "#FFFFC857", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf0f"), "Invisible", "INVIS", "#FF9BE7FF", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf10"), "Paralyzed", "PARALYZED", "#FFFF6B6B", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf11"), "Petrified", "PETRIFIED", "#FFB5C18E", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf12"), "Poisoned", "POISON", "#FF4ADE80", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf13"), "Prone", "PRONE", "#FF93C5FD", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf14"), "Restrained", "RESTRAIN", "#FF60A5FA", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf15"), "Stunned", "STUN", "#FFF59E0B", true, false),
            new ConditionDefinition(Guid.Parse("d0a70a1b-4d49-4cc8-8f4b-1e1c2a7abf16"), "Unconscious", "UNCON", "#FF9CA3AF", true, false),
        };
    }
}
