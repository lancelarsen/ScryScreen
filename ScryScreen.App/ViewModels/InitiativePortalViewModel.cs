using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ScryScreen.App.Services;
using ScryScreen.Core.InitiativeTracker;

namespace ScryScreen.App.ViewModels;

public enum InitiativePortalFontSize
{
    Small,
    Medium,
    Large,
}

public sealed partial class InitiativePortalViewModel : ObservableObject
{
    private const int MaxEntries = 14;

    private const string UnnamedToken = "(Unnamed)";

    public InitiativePortalViewModel(InitiativeTrackerState state)
    {
        Update(state);
    }

    [ObservableProperty]
    private double overlayOpacity;

    [ObservableProperty]
    private InitiativePortalFontSize portalFontSize = InitiativePortalFontSize.Medium;

    // Overlay slider should only affect the dark panels behind the text.
    // Keep the full-screen scrim at 0 so the media remains unchanged.
    public double OverlayScrimOpacity => 0.0;

    public double OverlayPanelOpacity => Math.Clamp(OverlayOpacity, 0.0, 1.0);

    public double EntryFontSize => PortalFontSize switch
    {
        InitiativePortalFontSize.Small => 34,
        InitiativePortalFontSize.Medium => 44,
        _ => 54,
    };

    public double InitFontSize => PortalFontSize switch
    {
        InitiativePortalFontSize.Small => 38,
        InitiativePortalFontSize.Medium => 48,
        _ => 58,
    };

    public double ModFontSize => PortalFontSize switch
    {
        InitiativePortalFontSize.Small => 24,
        InitiativePortalFontSize.Medium => 32,
        _ => 40,
    };

    public double RoundFontSize => PortalFontSize switch
    {
        _ => 26,
    };

    public double MoreFontSize => PortalFontSize switch
    {
        InitiativePortalFontSize.Small => 18,
        InitiativePortalFontSize.Medium => 24,
        _ => 28,
    };

    [ObservableProperty]
    private int round;

    [ObservableProperty]
    private IReadOnlyList<InitiativePortalEntryViewModel> entries = Array.Empty<InitiativePortalEntryViewModel>();

    [ObservableProperty]
    private int hiddenOmittedCount;

    [ObservableProperty]
    private int additionalEntriesCount;

    public bool HasAdditionalEntries => AdditionalEntriesCount > 0;

    partial void OnOverlayOpacityChanged(double value)
    {
        OnPropertyChanged(nameof(OverlayScrimOpacity));
        OnPropertyChanged(nameof(OverlayPanelOpacity));
    }

    partial void OnPortalFontSizeChanged(InitiativePortalFontSize value)
    {
        OnPropertyChanged(nameof(EntryFontSize));
        OnPropertyChanged(nameof(InitFontSize));
        OnPropertyChanged(nameof(ModFontSize));
        OnPropertyChanged(nameof(RoundFontSize));
        OnPropertyChanged(nameof(MoreFontSize));
    }

    public void Update(InitiativeTrackerState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        // Load on update so portals reflect condition config edits immediately.
        var conditionLibrary = ConditionLibraryService.LoadOrCreateDefault();

        Round = Math.Max(1, state.Round);

        var all = state.Entries ?? Array.Empty<InitiativeEntry>();
        var visible = all
            .Where(e =>
                !e.IsHidden
                && !string.IsNullOrWhiteSpace(e.Name)
                && !string.Equals(e.Name.Trim(), UnnamedToken, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        HiddenOmittedCount = all.Length - visible.Length;

        Guid? activeId = state.ActiveId;
        if (visible.Length > 0 && (!activeId.HasValue || visible.All(e => e.Id != activeId.Value)))
        {
            // If the state's active entry is omitted from the portal (blank/unnamed/hidden),
            // show the gem on the first visible entry instead.
            activeId = visible[0].Id;
        }

        var limited = visible
            .Take(MaxEntries)
            .Select(e => new InitiativePortalEntryViewModel(
                Id: e.Id,
                Name: e.Name.Trim(),
                Initiative: e.Initiative,
                Mod: e.Mod,
                IsActive: activeId.HasValue && e.Id == activeId.Value,
                HealthIconValue: ComputeHealthIconValue(e, conditionLibrary),
                HealthIconFontSize: ComputeHealthIconFontSize(e, conditionLibrary),
                HealthSvgPath: ComputeHealthSvgPath(e, conditionLibrary),
                HealthSvgSize: ComputeHealthSvgSize(e, conditionLibrary),
                HealthDotColorHex: ComputeHealthDotColorHex(e, conditionLibrary),
                IsStrikethrough: ComputeNameIsStrikethrough(e, conditionLibrary),
                Conditions: BuildConditionTags(e, conditionLibrary)))
            .ToArray();

        Entries = limited;
        AdditionalEntriesCount = Math.Max(0, visible.Length - limited.Length);

        OnPropertyChanged(nameof(HasAdditionalEntries));
    }

    private static IReadOnlyList<InitiativePortalConditionTagViewModel> BuildConditionTags(
        InitiativeEntry entry,
        ConditionLibraryService conditionLibrary)
    {
        var applied = entry.GetConditionsOrEmpty();
        if (applied.Length == 0)
        {
            return Array.Empty<InitiativePortalConditionTagViewModel>();
        }

        var tags = new List<InitiativePortalConditionTagViewModel>(applied.Length);
        foreach (var c in applied)
        {
            if (!conditionLibrary.TryGet(c.ConditionId, out var def))
            {
                continue;
            }

            // Manual-only conditions show no timer badge.
            var rounds = def.IsManualOnly ? null : c.RoundsRemaining;
            tags.Add(new InitiativePortalConditionTagViewModel(
                Name: def.Name,
                ColorHex: def.ColorHex,
                RoundsRemaining: rounds));
        }

        return tags;
    }

    private static bool HasConditionId(InitiativeEntry entry, Guid conditionId)
        => entry.GetConditionsOrEmpty().Any(c => c.ConditionId == conditionId);

    private static bool ComputeNameIsStrikethrough(InitiativeEntry entry, ConditionLibraryService conditionLibrary)
    {
        // DEAD condition always strikes.
        if (HasConditionId(entry, ConditionLibraryService.DeadId))
        {
            return true;
        }

        var current = TryParseInt(entry.CurrentHp);
        if (current.HasValue && current.Value <= 0)
        {
            return true;
        }

        return false;
    }

    private enum HealthIndicatorKind
    {
        None,
        Dead,
        Bloodied,
        Injured,
        Healthy,
    }

    private static HealthIndicatorKind ComputeHealthIndicatorKind(InitiativeEntry entry, ConditionLibraryService conditionLibrary)
    {
        // Dead takes precedence.
        if (HasConditionId(entry, ConditionLibraryService.DeadId))
        {
            return HealthIndicatorKind.Dead;
        }

        var current = TryParseInt(entry.CurrentHp);
        if (current.HasValue && current.Value <= 0)
        {
            return HealthIndicatorKind.Dead;
        }

        // Bloodied condition forces bloodied.
        if (HasConditionId(entry, ConditionLibraryService.BloodiedId))
        {
            return HealthIndicatorKind.Bloodied;
        }

        var max = TryParseInt(entry.MaxHp);
        if (!max.HasValue || max.Value <= 0)
        {
            // Unknown max => show no indicator.
            return HealthIndicatorKind.None;
        }

        if (!current.HasValue)
        {
            // Max-only implies full/healthy.
            return HealthIndicatorKind.Healthy;
        }

        // Full HP.
        if (current.Value >= max.Value)
        {
            return HealthIndicatorKind.Healthy;
        }

        // 50% is bloodied.
        if (current.Value * 2 <= max.Value)
        {
            return HealthIndicatorKind.Bloodied;
        }

        // Injured (above 50% but not full).
        return HealthIndicatorKind.Injured;
    }

    private static string? ComputeHealthIconValue(InitiativeEntry entry, ConditionLibraryService conditionLibrary)
    {
        return null;
    }

    private static double ComputeHealthIconFontSize(InitiativeEntry entry, ConditionLibraryService conditionLibrary)
    {
        return 0;
    }

    private static string? ComputeHealthSvgPath(InitiativeEntry entry, ConditionLibraryService conditionLibrary)
    {
        return ComputeHealthIndicatorKind(entry, conditionLibrary) switch
        {
            HealthIndicatorKind.Dead => "/Assets/Icons/skull.svg",
            HealthIndicatorKind.Bloodied => "/Assets/Icons/water_drop.svg",
            HealthIndicatorKind.Healthy => "/Assets/Icons/sword_rose.svg",
            HealthIndicatorKind.Injured => "/Assets/Icons/healing.svg",
            _ => null,
        };
    }

    private static double ComputeHealthSvgSize(InitiativeEntry entry, ConditionLibraryService conditionLibrary)
    {
        return ComputeHealthIndicatorKind(entry, conditionLibrary) switch
        {
            HealthIndicatorKind.Dead => 36,
            HealthIndicatorKind.Bloodied => 36,
            HealthIndicatorKind.Healthy => 36,
            HealthIndicatorKind.Injured => 36,
            _ => 0,
        };
    }

    private static string? ComputeHealthDotColorHex(InitiativeEntry entry, ConditionLibraryService conditionLibrary)
    {
        return ComputeHealthIndicatorKind(entry, conditionLibrary) switch
        {
            HealthIndicatorKind.Dead => "#FFFFFFFF",
            HealthIndicatorKind.Bloodied => "#FFDC2626",
            HealthIndicatorKind.Injured => "#FFF59E0B",
            HealthIndicatorKind.Healthy => "#FF22C55E",
            _ => null,
        };
    }

    private static int? TryParseInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return int.TryParse(text.Trim(), out var value) ? value : null;
    }
}

public sealed record InitiativePortalEntryViewModel(
    Guid Id,
    string Name,
    int Initiative,
    int Mod,
    bool IsActive,
    string? HealthIconValue,
    double HealthIconFontSize,
    string? HealthSvgPath,
    double HealthSvgSize,
    string? HealthDotColorHex,
    bool IsStrikethrough,
    IReadOnlyList<InitiativePortalConditionTagViewModel> Conditions)
{
    public bool HasMod => Mod != 0;

    public bool HasHealthDot => !string.IsNullOrWhiteSpace(HealthDotColorHex);

    public bool HasHealthIcon => !string.IsNullOrWhiteSpace(HealthIconValue);

    public bool HasHealthSvg => !string.IsNullOrWhiteSpace(HealthSvgPath);

    public IBrush HealthDotBrush
    {
        get
        {
            if (string.IsNullOrWhiteSpace(HealthDotColorHex))
            {
                return Brushes.Transparent;
            }

            try
            {
                return Brush.Parse(HealthDotColorHex);
            }
            catch
            {
                return Brushes.Transparent;
            }
        }
    }

    public TextDecorationCollection? NameDecorations
        => IsStrikethrough ? TextDecorations.Strikethrough : null;

    public string InitiativeDisplay => Initiative.ToString();

    public string ModDisplay
    {
        get
        {
            if (Mod == 0)
            {
                return string.Empty;
            }

            var modText = Mod > 0 ? $"+{Mod}" : Mod.ToString();
            return $"({modText})";
        }
    }
}

public sealed record InitiativePortalConditionTagViewModel(
    string Name,
    string ColorHex,
    int? RoundsRemaining)
{
    public IBrush Brush
    {
        get
        {
            try
            {
                return Avalonia.Media.Brush.Parse(ColorHex);
            }
            catch
            {
                return Avalonia.Media.Brushes.White;
            }
        }
    }

    public bool HasTimer => RoundsRemaining.HasValue;
}
