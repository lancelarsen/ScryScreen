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

            var initiativeJson = vm.InitiativeTracker.ExportConfigJson(indented: true);
            File.WriteAllText(GetInitiativeConfigPath(), initiativeJson);

            // Effects config is tied to a selected media item.
            // If nothing is selected yet, keep the last saved effects file as-is.
            var effectsJson = vm.ExportSelectedEffectsConfigJson(indented: true);
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
            var initiativePath = GetInitiativeConfigPath();
            if (File.Exists(initiativePath))
            {
                var json = File.ReadAllText(initiativePath);
                vm.InitiativeTracker.ImportConfigJson(json);
            }

            var statePath = GetStatePath();
            LastSessionState? state = null;
            if (File.Exists(statePath))
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

            if (!string.IsNullOrWhiteSpace(state?.LastMediaFolderPath) && Directory.Exists(state.LastMediaFolderPath))
            {
                vm.ImportMediaFolder(state.LastMediaFolderPath);
            }

            if (!string.IsNullOrWhiteSpace(state?.LastInitiativeConfigSaveFileName))
            {
                vm.LastInitiativeConfigSaveFileName = state.LastInitiativeConfigSaveFileName;
            }

            if (!string.IsNullOrWhiteSpace(state?.LastEffectsConfigSaveFileName))
            {
                vm.LastEffectsConfigSaveFileName = state.LastEffectsConfigSaveFileName;
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

            // Apply effects config to the currently selected media item (if any).
            var effectsPath = GetEffectsConfigPath();
            if (File.Exists(effectsPath))
            {
                var json = File.ReadAllText(effectsPath);
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
