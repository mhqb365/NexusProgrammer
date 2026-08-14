using System.IO;
using System.Text.Json;

namespace NexusProgrammer;

public sealed class AppSettings
{
    public string MeRegionRoot { get; set; } = string.Empty;
    public string FitRoot { get; set; } = string.Empty;
}

internal static class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NexusProgrammer",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
