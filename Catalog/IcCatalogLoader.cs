using System.IO;

namespace NexusProgrammer;

public static class IcCatalogLoader
{
    private static readonly CatalogSource[] CatalogFiles =
    [
        new("IntegratedICCatalog.tsv", Path.Combine("Catalog", "CH34x_SPI_NOR.tsv")),
        new("T48ICCatalog.tsv", Path.Combine("Catalog", "XGecuT48_SPI_NOR.tsv")),
        new("User_SPI_NOR.tsv", Path.Combine("Catalog", "User_SPI_NOR.tsv"))
    ];

    public static List<IcCandidate> LoadSpiCatalog()
    {
        var list = new List<IcCandidate>();
        var knownIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in CatalogFiles)
        {
            AddTsvCatalog(list, knownIds, source);
        }

        return list;
    }

    public static void SaveUserCandidate(IcCandidate candidate)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Catalog", "User_SPI_NOR.tsv");
        if (!Directory.Exists(Path.GetDirectoryName(path)))
        {
            path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Catalog", "User_SPI_NOR.tsv");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
        using var writer = new StreamWriter(path, append: true);
        if (writeHeader)
        {
            writer.WriteLine("# User-added SPI NOR catalog");
            writer.WriteLine("Device\tManufacturer\tRawId\tSizeBytes\tPageSize\tVolts\tProtocol\tCommandSet\tType\tSupported");
        }

        writer.WriteLine(string.Join('\t',
            candidate.Device,
            candidate.Manuf,
            candidate.JedecId.Replace(" ", string.Empty, StringComparison.Ordinal),
            candidate.Profile.SizeBytes,
            candidate.Profile.PageSize,
            candidate.Volts.TrimEnd('V'),
            candidate.Profile.Protocol,
            candidate.Profile.CommandSet,
            candidate.Type,
            "true"));
    }

    private static void AddTsvCatalog(List<IcCandidate> list, Dictionary<string, string> knownIds, CatalogSource source)
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
                !string.Equals(fields[6], "SPI", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var device = fields[0];
            var rawId = FormatRawId(fields[2]);
            if (string.IsNullOrWhiteSpace(rawId) && knownIds.TryGetValue(DeviceKey(device), out var knownId))
            {
                rawId = knownId;
            }

            var volts = FormatVolts(device, fields[5]);
            var profile = new ChipProfile(
                device,
                fields[6],
                sizeBytes,
                pageSize,
                fields[7],
                string.IsNullOrWhiteSpace(fields[1]) ? "GENERIC" : fields[1].ToUpperInvariant(),
                volts,
                fields[8]);

            list.Add(new IcCandidate(
                device,
                volts,
                FormatMbits(sizeBytes),
                $"{pageSize} Bytes",
                profile.Manufacturer,
                fields[8],
                profile,
                rawId));

            if (!string.IsNullOrWhiteSpace(rawId))
            {
                knownIds.TryAdd(DeviceKey(device), rawId);
            }
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

    private static string FormatVolts(string device, string? volts)
    {
        var normalizedDevice = device.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
        if (normalizedDevice.Contains("1.8", StringComparison.OrdinalIgnoreCase) ||
            normalizedDevice.Contains("1V8", StringComparison.OrdinalIgnoreCase))
        {
            return "1.8V";
        }

        return string.IsNullOrWhiteSpace(volts) ? string.Empty : volts.EndsWith('V') ? volts : $"{volts}V";
    }

    private static string DeviceKey(string device)
    {
        var name = device.Trim();
        var slash = name.IndexOf('/');
        if (slash >= 0)
        {
            name = name[..slash];
        }

        var paren = name.IndexOf('(');
        if (paren >= 0)
        {
            name = name[..paren];
        }

        return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    public static string FormatMbits(int bytes)
    {
        var bits = bytes * 8.0;
        return bits >= 1024 * 1024
            ? $"{bits / (1024 * 1024):0.#} Mbits"
            : $"{bits / 1024:0.#} Kbits";
    }

    private sealed record CatalogSource(string OutputFileName, string SourcePath);
}
