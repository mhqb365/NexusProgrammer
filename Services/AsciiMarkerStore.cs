using System.IO;
using System.Text.Json;

namespace NexusProgrammer;

internal static class AsciiMarkerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string MarkerPath => Path.Combine(AppContext.BaseDirectory, "AsciiMarker.json");

    public static List<HexMarker> Load()
    {
        try
        {
            return File.Exists(MarkerPath)
                ? Normalize(JsonSerializer.Deserialize<List<HexMarker>>(File.ReadAllText(MarkerPath)) ?? [])
                : [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IEnumerable<HexMarker> markers)
    {
        File.WriteAllText(MarkerPath, JsonSerializer.Serialize(Normalize(markers), JsonOptions));
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
}
