using System.IO;

namespace NexusProgrammer;

public static class IcCatalogLoader
{
    private static readonly CatalogSource[] CatalogFiles =
    [
        new("IntegratedICCatalog.tsv", Path.Combine("Catalog", "CH34x_SPI_NOR.tsv")),
        new("T48ICCatalog.tsv", Path.Combine("Catalog", "XGecuT48_SPI_NOR.tsv"))
    ];

    public static List<IcCandidate> LoadSpiCatalog()
    {
        var list = new List<IcCandidate>();
        var knownDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in CatalogFiles)
        {
            AddTsvCatalog(list, knownDevices, source);
        }

        return list;
    }

    private static void AddTsvCatalog(List<IcCandidate> list, HashSet<string> knownDevices, CatalogSource source)
    {
        var catalogPath = FindCatalogFile(source);
        if (catalogPath is null)
        {
            return;
        }

        foreach (var line in File.ReadLines(catalogPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith("Device\t"))
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length < 10 ||
                !int.TryParse(fields[3], out var sizeBytes) ||
                !int.TryParse(fields[4], out var pageSize) ||
                sizeBytes <= 0 ||
                pageSize <= 0 ||
                !string.Equals(fields[6], "SPI", StringComparison.OrdinalIgnoreCase) ||
                !knownDevices.Add(fields[0]))
            {
                continue;
            }

            var profile = new ChipProfile(
                fields[0],
                fields[6],
                sizeBytes,
                pageSize,
                fields[7],
                string.IsNullOrWhiteSpace(fields[1]) ? "GENERIC" : fields[1].ToUpperInvariant(),
                FormatVolts(fields[5]),
                fields[8]);

            list.Add(new IcCandidate(
                fields[0],
                FormatVolts(fields[5]),
                FormatMbits(sizeBytes),
                $"{pageSize} Bytes",
                profile.Manufacturer,
                fields[8],
                profile,
                FormatRawId(fields[2])));
        }
    }

    private static string? FindCatalogFile(CatalogSource source)
    {
        var catalogPath = Path.Combine(AppContext.BaseDirectory, source.OutputFileName);
        if (File.Exists(catalogPath))
        {
            return catalogPath;
        }

        catalogPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", source.SourcePath);
        return File.Exists(catalogPath) ? catalogPath : null;
    }

    private static string FormatRawId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        var hex = new string(id.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return string.Join(" ", Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)));
    }

    private static string FormatVolts(string? volts) =>
        string.IsNullOrWhiteSpace(volts) ? string.Empty : volts.EndsWith('V') ? volts : $"{volts}V";

    public static string FormatMbits(int bytes)
    {
        var bits = bytes * 8.0;
        return bits >= 1024 * 1024
            ? $"{bits / (1024 * 1024):0.#} Mbits"
            : $"{bits / 1024:0.#} Kbits";
    }

    private sealed record CatalogSource(string OutputFileName, string SourcePath);
}
