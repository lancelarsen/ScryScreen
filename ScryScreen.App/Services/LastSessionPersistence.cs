using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Threading;
using ScryScreen.App.Models;
using ScryScreen.App.ViewModels;

namespace ScryScreen.App.Services;

public static class LastSessionPersistence
{
    private const string SavesFolderName = "saves";
    private const string InitiativeFileName = "initiative.json";
    private const string EffectsFileName = "effects.json";
    private const string StateFileName = "last_session.json";

    private sealed class LastSessionState
    {
        public string? LastMediaFolderPath { get; set; }
        public string? LastSelectedMediaPath { get; set; }
        public string? LastInitiativeConfigSaveFileName { get; set; }
        public string? LastEffectsConfigSaveFileName { get; set; }
        public string? LastInitiativeConfigSavePath { get; set; }
        public string? LastEffectsConfigSavePath { get; set; }
        public bool AutoSaveInitiativeEnabled { get; set; }
        public bool AutoSaveEffectsEnabled { get; set; }

        public int HourglassDurationMinutes { get; set; }
        public int HourglassDurationSeconds { get; set; }
        public double HourglassOverlayOpacity { get; set; }
        public bool HourglassPlaySoundsEnabled { get; set; }

        public int? HourglassParticleCount { get; set; }
        public double? HourglassDensity { get; set; }
        public double? HourglassParticleSize { get; set; }

        public double? MapMasterPlayerMaskOpacity { get; set; }
        public double? MapMasterGmMaskOpacity { get; set; }
        public double? MapMasterEraserDiameter { get; set; }
        public double? MapMasterEraserHardness { get; set; }
        public string? MapMasterMaskType { get; set; }

        public string? DiceRollerRollDirection { get; set; }
        public bool DiceRollerShowDebugInfo { get; set; }
        public List<DiceRollerDieConfig>? DiceRollerDieConfigs { get; set; }

        public DateTimeOffset SavedAtUtc { get; set; }
    }

    private sealed class DiceRollerDieConfig
    {
        public int Sides { get; set; }
        public double DieScale { get; set; }
        public double NumberScale { get; set; }
    }

    private static int Clamp(int value, int min, int max)
        => value < min ? min : (value > max ? max : value);

    private static double Clamp(double value, double min, double max)
        => value < min ? min : (value > max ? max : value);

    private static int ParseInt(string? text)
        => int.TryParse(text, out var v) ? v : 0;

    private static LastSessionState BuildState(MainWindowViewModel vm)
    {
        var physics = vm.Hourglass.SnapshotState().Physics;

        return new LastSessionState
        {
            LastMediaFolderPath = vm.LastMediaFolderPath,
            LastSelectedMediaPath = vm.LastSelectedMediaPath,
            LastInitiativeConfigSaveFileName = vm.LastInitiativeConfigSaveFileName,
            LastEffectsConfigSaveFileName = vm.LastEffectsConfigSaveFileName,
            LastInitiativeConfigSavePath = vm.LastInitiativeConfigSavePath,
            LastEffectsConfigSavePath = vm.LastEffectsConfigSavePath,
            AutoSaveInitiativeEnabled = vm.AutoSaveInitiativeEnabled,
            AutoSaveEffectsEnabled = vm.AutoSaveEffectsEnabled,

            HourglassDurationMinutes = Clamp(ParseInt(vm.Hourglass.DurationMinutesText), 0, 999),
            HourglassDurationSeconds = Clamp(ParseInt(vm.Hourglass.DurationSecondsText), 0, 59),
            HourglassOverlayOpacity = Clamp(vm.Hourglass.OverlayOpacity, 0, 1),
            HourglassPlaySoundsEnabled = vm.Hourglass.PlaySoundsEnabled,

            HourglassParticleCount = physics.ParticleCount,
            HourglassDensity = physics.Density,
            HourglassParticleSize = physics.ParticleSize,

            MapMasterPlayerMaskOpacity = Clamp(vm.MapMaster.PlayerMaskOpacity, 0, 1),
            MapMasterGmMaskOpacity = Clamp(vm.MapMaster.GmMaskOpacity, 0, 1),
            MapMasterEraserDiameter = Clamp(vm.MapMaster.EraserDiameter, 2, 80),
            MapMasterEraserHardness = Clamp(vm.MapMaster.EraserHardness, 0, 1),
            MapMasterMaskType = vm.MapMaster.SelectedMaskType.ToString(),

            DiceRollerRollDirection = vm.DiceRoller.RollDirection.ToString(),
            DiceRollerShowDebugInfo = vm.DiceRoller.ShowDebugInfo,
            DiceRollerDieConfigs = vm.DiceRoller.DiceVisualConfigs
                .Select(c => new DiceRollerDieConfig
                {
                    Sides = c.Sides,
                    DieScale = Clamp(c.DieScale, 0.5, 1.75),
                    NumberScale = Clamp(c.NumberScale, 0.5, 2.0),
                })
                .OrderBy(c => c.Sides)
                .ToList(),

            SavedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static void WriteStateFile(MainWindowViewModel vm)
    {
        var state = BuildState(vm);
        File.WriteAllText(GetStatePath(), JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }

    public static void SaveStateOnly(MainWindowViewModel vm)
    {
        if (vm is null)
        {
            return;
        }

        try
        {
            var dir = GetSavesDirectory();
            if (!TryEnsureDirectory(dir))
            {
                return;
            }

            WriteStateFile(vm);
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "Auto-save last session");
        }
    }

    public static string GetSavesDirectory()
    {
        // Primary: alongside the app binaries (works well for this "project-local" app).
        var primary = Path.Combine(AppContext.BaseDirectory, SavesFolderName);
        if (TryEnsureDirectory(primary))
        {
            return primary;
        }

        // Fallback: user-writable location.
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScryScreen",
            SavesFolderName);

        TryEnsureDirectory(local);
        return local;
    }

    public static string GetInitiativeConfigPath() => Path.Combine(GetSavesDirectory(), InitiativeFileName);
    public static string GetEffectsConfigPath() => Path.Combine(GetSavesDirectory(), EffectsFileName);
    public static string GetStatePath() => Path.Combine(GetSavesDirectory(), StateFileName);

    public static void Save(MainWindowViewModel vm)
    {
        if (vm is null)
        {
            return;
        }

        try
        {
            var dir = GetSavesDirectory();
            if (!TryEnsureDirectory(dir))
            {
                return;
            }

            var initiativeJson = vm.ExportBestInitiativeConfigJson(indented: true);
            File.WriteAllText(GetInitiativeConfigPath(), initiativeJson);

            // Effects config is global.
            var effectsJson = vm.ExportBestEffectsConfigJson(indented: true);
            if (!string.IsNullOrWhiteSpace(effectsJson))
            {
                File.WriteAllText(GetEffectsConfigPath(), effectsJson);
            }

            WriteStateFile(vm);
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "Save last session");
        }
    }

    public static void Load(MainWindowViewModel vm)
    {
        if (vm is null)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Load(vm));
            return;
        }

        try
        {
            using var _ = vm.SuppressAutoSave();

            var statePath = GetStatePath();
            var hasStateFile = File.Exists(statePath);
            LastSessionState? state = null;
            if (hasStateFile)
            {
                try
                {
                    state = JsonSerializer.Deserialize<LastSessionState>(File.ReadAllText(statePath));
                }
                catch
                {
                    state = null;
                }
            }

            // First run (or state file couldn't be read): default auto-save ON and target the default saves files.
            if (!hasStateFile || state is null)
            {
                vm.LastInitiativeConfigSavePath ??= GetInitiativeConfigPath();
                vm.LastEffectsConfigSavePath ??= GetEffectsConfigPath();
                vm.LastInitiativeConfigSaveFileName ??= Path.GetFileName(vm.LastInitiativeConfigSavePath);
                vm.LastEffectsConfigSaveFileName ??= Path.GetFileName(vm.LastEffectsConfigSavePath);
                vm.AutoSaveInitiativeEnabled = true;
                vm.AutoSaveEffectsEnabled = true;
                vm.Hourglass.PlaySoundsEnabled = true;
            }

            // Backfill save paths if the state file predates these fields.
            vm.LastInitiativeConfigSavePath ??= state?.LastInitiativeConfigSavePath ?? GetInitiativeConfigPath();
            vm.LastEffectsConfigSavePath ??= state?.LastEffectsConfigSavePath ?? GetEffectsConfigPath();
            vm.LastInitiativeConfigSaveFileName ??= state?.LastInitiativeConfigSaveFileName ?? Path.GetFileName(vm.LastInitiativeConfigSavePath);
            vm.LastEffectsConfigSaveFileName ??= state?.LastEffectsConfigSaveFileName ?? Path.GetFileName(vm.LastEffectsConfigSavePath);

            // Prefer loading the initiative config from the last save path (auto-save target),
            // but fall back to the default file in saves/.
            var initiativeLoadPath =
                !string.IsNullOrWhiteSpace(vm.LastInitiativeConfigSavePath) && File.Exists(vm.LastInitiativeConfigSavePath)
                    ? vm.LastInitiativeConfigSavePath
                    : GetInitiativeConfigPath();

            if (!string.IsNullOrWhiteSpace(initiativeLoadPath) && File.Exists(initiativeLoadPath))
            {
                var json = File.ReadAllText(initiativeLoadPath);
                vm.InitiativeTracker.ImportConfigJson(json);
            }

            if (!string.IsNullOrWhiteSpace(state?.LastMediaFolderPath) && Directory.Exists(state.LastMediaFolderPath))
            {
                vm.ImportMediaFolder(state.LastMediaFolderPath);
            }

            if (state is not null)
            {
                vm.AutoSaveInitiativeEnabled = state.AutoSaveInitiativeEnabled;
                vm.AutoSaveEffectsEnabled = state.AutoSaveEffectsEnabled;

                // Hourglass: restore last set duration + overlay opacity.
                vm.Hourglass.DurationMinutesText = Clamp(state.HourglassDurationMinutes, 0, 999).ToString();
                vm.Hourglass.DurationSecondsText = Clamp(state.HourglassDurationSeconds, 0, 59).ToString();
                vm.Hourglass.OverlayOpacity = Clamp(state.HourglassOverlayOpacity, 0, 1);
                vm.Hourglass.PlaySoundsEnabled = state.HourglassPlaySoundsEnabled;

                // Hourglass: restore sand physics if present (state files prior to these fields should not clobber defaults).
                if (state.HourglassParticleCount is not null)
                {
                    vm.Hourglass.ParticleCountText = Clamp(state.HourglassParticleCount.Value, 50, 8000).ToString();
                }

                if (state.HourglassDensity is not null)
                {
                    vm.Hourglass.DensityText = Clamp(state.HourglassDensity.Value, 0, 10).ToString("0.00");
                }

                if (state.HourglassParticleSize is not null)
                {
                    vm.Hourglass.ParticleSizeText = Clamp(state.HourglassParticleSize.Value, 2, 14).ToString("0.0");
                }

                // Map Master preferences
                if (state.MapMasterPlayerMaskOpacity is not null)
                {
                    vm.MapMaster.PlayerMaskOpacity = Clamp(state.MapMasterPlayerMaskOpacity.Value, 0, 1);
                }

                if (state.MapMasterGmMaskOpacity is not null)
                {
                    vm.MapMaster.GmMaskOpacity = Clamp(state.MapMasterGmMaskOpacity.Value, 0, 1);
                }

                if (state.MapMasterEraserDiameter is not null)
                {
                    vm.MapMaster.EraserDiameter = Clamp(state.MapMasterEraserDiameter.Value, 2, 80);
                }

                if (state.MapMasterEraserHardness is not null)
                {
                    vm.MapMaster.EraserHardness = Clamp(state.MapMasterEraserHardness.Value, 0, 1);
                }

                if (!string.IsNullOrWhiteSpace(state.MapMasterMaskType)
                    && Enum.TryParse<ScryScreen.App.Models.MapMasterMaskType>(state.MapMasterMaskType, ignoreCase: true, out var mt))
                {
                    vm.MapMaster.SelectedMaskType = mt;
                }

                // Dice Tray preferences
                if (!string.IsNullOrWhiteSpace(state.DiceRollerRollDirection)
                    && Enum.TryParse<DiceRollDirection>(state.DiceRollerRollDirection, ignoreCase: true, out var dir))
                {
                    vm.DiceRoller.RollDirection = dir;
                }

                vm.DiceRoller.ShowDebugInfo = state.DiceRollerShowDebugInfo;

                if (state.DiceRollerDieConfigs is not null)
                {
                    foreach (var cfg in state.DiceRollerDieConfigs)
                    {
                        var target = vm.DiceRoller.DiceVisualConfigs.FirstOrDefault(c => c.Sides == cfg.Sides);
                        if (target is null)
                        {
                            continue;
                        }

                        target.DieScale = Clamp(cfg.DieScale, 0.5, 1.75);
                        target.NumberScale = Clamp(cfg.NumberScale, 0.5, 2.0);
                    }
                }
            }

            // Restore selected media (if it exists in the loaded folder).
            if (!string.IsNullOrWhiteSpace(state?.LastSelectedMediaPath) && vm.Media.Items.Count > 0)
            {
                var match = vm.Media.Items.FirstOrDefault(i =>
                    string.Equals(i.FilePath, state.LastSelectedMediaPath, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    vm.Media.SelectedItem = match;
                }
            }

            // Apply effects config globally. Prefer loading from the last save path (auto-save target);
            // fall back to saves/effects.json.
            var effectsLoadPath =
                !string.IsNullOrWhiteSpace(vm.LastEffectsConfigSavePath) && File.Exists(vm.LastEffectsConfigSavePath)
                    ? vm.LastEffectsConfigSavePath
                    : GetEffectsConfigPath();

            if (!string.IsNullOrWhiteSpace(effectsLoadPath) && File.Exists(effectsLoadPath))
            {
                var json = File.ReadAllText(effectsLoadPath);
                vm.ImportSelectedEffectsConfigJson(json);
            }
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "Load last session");
        }
    }

    private static bool TryEnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            // Light touch write test to catch non-writable locations.
            var testPath = Path.Combine(path, ".write_test");
            File.WriteAllText(testPath, "ok");
            File.Delete(testPath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
