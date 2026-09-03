using System.IO;
using System.Text.Json;

namespace NexusProgrammer;

internal static class HexFillPresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string PresetPath => Path.Combine(AppContext.BaseDirectory, "HexFill.json");

    private static List<HexFillPreset> DefaultPresets() =>
    [
        new() { Name = "00", Hex = "00" },
        new() { Name = "FF", Hex = "FF" }
    ];

    public static List<HexFillPreset> Load()
    {
        try
        {
            if (!File.Exists(PresetPath))
            {
                var initialPresets = DefaultPresets();
                Save(initialPresets);
                return initialPresets;
            }

            var presets = Normalize(JsonSerializer.Deserialize<List<HexFillPreset>>(File.ReadAllText(PresetPath)) ?? []);
            if (presets.Count > 0)
            {
                return presets;
            }

            var defaults = DefaultPresets();
            Save(defaults);
            return defaults;
        }
        catch
        {
            return DefaultPresets();
        }
    }

    public static void Save(IEnumerable<HexFillPreset> presets)
    {
        File.WriteAllText(PresetPath, JsonSerializer.Serialize(Normalize(presets), JsonOptions));
    }

    private static List<HexFillPreset> Normalize(IEnumerable<HexFillPreset> presets) =>
        presets
            .Where(preset => !string.IsNullOrWhiteSpace(preset.Name) && !string.IsNullOrWhiteSpace(preset.Hex))
            .Select(preset => new HexFillPreset
            {
                Name = preset.Name.Trim(),
                Hex = preset.Hex.Trim()
            })
            .ToList();
}
