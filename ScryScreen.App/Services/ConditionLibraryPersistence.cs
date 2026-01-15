using System;
using System.IO;
using System.Text.Json;
using ScryScreen.App.Models;

namespace ScryScreen.App.Services;

public static class ConditionLibraryPersistence
{
    private const string ConditionLibraryFileName = "conditions.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
    };

    public static string GetConditionLibraryPath()
        => Path.Combine(LastSessionPersistence.GetSavesDirectory(), ConditionLibraryFileName);

    public static ConditionLibraryConfig LoadOrDefault()
    {
        try
        {
            var path = GetConditionLibraryPath();
            if (!File.Exists(path))
            {
                return new ConditionLibraryConfig();
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ConditionLibraryConfig();
            }

            return JsonSerializer.Deserialize<ConditionLibraryConfig>(json, JsonOptions) ?? new ConditionLibraryConfig();
        }
        catch
        {
            return new ConditionLibraryConfig();
        }
    }

    public static void Save(ConditionLibraryConfig config)
    {
        if (config is null)
        {
            return;
        }

        try
        {
            var path = GetConditionLibraryPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "Save condition library");
        }
    }
}
