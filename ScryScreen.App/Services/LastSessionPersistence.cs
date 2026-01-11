using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Threading;
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
        public DateTimeOffset SavedAtUtc { get; set; }
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

            var state = new LastSessionState
            {
                LastMediaFolderPath = vm.LastMediaFolderPath,
                LastSelectedMediaPath = vm.LastSelectedMediaPath,
                LastInitiativeConfigSaveFileName = vm.LastInitiativeConfigSaveFileName,
                LastEffectsConfigSaveFileName = vm.LastEffectsConfigSaveFileName,
                LastInitiativeConfigSavePath = vm.LastInitiativeConfigSavePath,
                LastEffectsConfigSavePath = vm.LastEffectsConfigSavePath,
                AutoSaveInitiativeEnabled = vm.AutoSaveInitiativeEnabled,
                AutoSaveEffectsEnabled = vm.AutoSaveEffectsEnabled,
                SavedAtUtc = DateTimeOffset.UtcNow,
            };

            File.WriteAllText(GetStatePath(), JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
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
