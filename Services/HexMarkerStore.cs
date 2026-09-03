using System.IO;
using System.Text.Json;

namespace NexusProgrammer;

internal static class HexMarkerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string MarkerPath => Path.Combine(AppContext.BaseDirectory, "HexMarker.json");

    private static List<HexMarker> DefaultMarkers() =>
    [
        new()
        {
            Name = "Find Win Key",
            Hex = "01 00 00 00 00 00 00 00 01 00 00 00 00 00 00 00 1D 00 00 00"
        }
    ];

    public static List<HexMarker> Load()
    {
        try
        {
            if (File.Exists(MarkerPath))
            {
                var markers = Normalize(JsonSerializer.Deserialize<List<HexMarker>>(File.ReadAllText(MarkerPath)) ?? []);
                if (markers.Count > 0)
                {
                    return markers;
                }

                var defaults = DefaultMarkers();
                Save(defaults);
                return defaults;
            }

            var migrated = LoadLegacyMarkers();
            if (migrated.Count > 0)
            {
                Save(migrated);
                return migrated;
            }

            var initialMarkers = DefaultMarkers();
            Save(initialMarkers);
            return initialMarkers;
        }
        catch
        {
            return DefaultMarkers();
        }
    }

    public static void Save(IEnumerable<HexMarker> markers)
    {
        var normalized = Normalize(markers);
        File.WriteAllText(MarkerPath, JsonSerializer.Serialize(normalized, JsonOptions));
    }

    private static List<HexMarker> Normalize(IEnumerable<HexMarker> markers) =>
        markers
            .Where(marker => !string.IsNullOrWhiteSpace(marker.Name) && !string.IsNullOrWhiteSpace(marker.Hex))
            .Select(marker => new HexMarker
            {
                Name = marker.Name.Trim(),
                Hex = marker.Hex.Trim()
            })
            .ToList();

    private static List<HexMarker> LoadLegacyMarkers()
    {
        try
        {
            if (!File.Exists(AppSettingsService.SettingsPath))
            {
                return [];
            }

            using var document = JsonDocument.Parse(File.ReadAllText(AppSettingsService.SettingsPath));
            if (!document.RootElement.TryGetProperty("HexMarkers", out var markersElement))
            {
                return [];
            }

            return Normalize(markersElement.Deserialize<List<HexMarker>>() ?? []);
        }
        catch
        {
            return [];
        }
    }
}
