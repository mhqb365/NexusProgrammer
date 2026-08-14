using System.IO;

namespace NexusProgrammer;

public static class IcCatalogLoader
{
    private const string CatalogFileName = "ICCatalog.tsv";
    private const string UserCatalogFileName = "User_SPI_NOR.tsv";
    private static readonly CatalogSource[] CatalogFiles =
    [
        new(Path.Combine("Catalog", "CH34x_SPI_NOR.tsv")),
        new(Path.Combine("Catalog", "XGecuT48_SPI_NOR.tsv")),
        new(Path.Combine("Catalog", "User_SPI_NOR.tsv"))
    ];

    public static List<IcCandidate> LoadSpiCatalog()
    {
        var list = new List<IcCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builtCatalog = Path.Combine(AppContext.BaseDirectory, CatalogFileName);
        if (File.Exists(builtCatalog))
        {
            AddTsvCatalog(list, seen, builtCatalog, isUserCatalog: false);
            var userCatalog = Path.Combine(AppContext.BaseDirectory, UserCatalogFileName);
            if (File.Exists(userCatalog))
            {
                AddTsvCatalog(list, seen, userCatalog, isUserCatalog: true);
            }

            return list;
        }

        foreach (var source in CatalogFiles)
        {
            var catalogPath = FindSourceCatalogFile(source);
            if (catalogPath is not null)
            {
                AddTsvCatalog(list, seen, catalogPath, IsUserCatalog(source.SourcePath));
            }
        }

        return list;
    }

    public static void SaveUserCandidate(IcCandidate candidate)
    {
        var path = Path.Combine(AppContext.BaseDirectory, UserCatalogFileName);

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

    public static void SaveUserCatalog(IEnumerable<IcCandidate> candidates)
    {
        var path = Path.Combine(AppContext.BaseDirectory, UserCatalogFileName);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new StreamWriter(path, append: false);
        writer.WriteLine("# User-added SPI NOR catalog");
        writer.WriteLine("Device\tManufacturer\tRawId\tSizeBytes\tPageSize\tVolts\tProtocol\tCommandSet\tType\tSupported");
        foreach (var candidate in candidates)
        {
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
    }

    private static void AddTsvCatalog(List<IcCandidate> list, HashSet<string> seen, string catalogPath, bool isUserCatalog)
    {
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
            if (string.IsNullOrWhiteSpace(rawId))
            {
                continue;
            }

            var candidateKey = string.Join('|', DeviceKey(device), rawId, sizeBytes, fields[5], fields[8]);
            if (!seen.Add(candidateKey))
            {
                continue;
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
                rawId,
                isUserCatalog));

        }
    }

    private static string? FindSourceCatalogFile(CatalogSource source)
    {
        var catalogPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", source.SourcePath);
        return File.Exists(catalogPath) ? catalogPath : null;
    }

    private static bool IsUserCatalog(string path) =>
        Path.GetFileName(path).Equals(UserCatalogFileName, StringComparison.OrdinalIgnoreCase);

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

    private sealed record CatalogSource(string SourcePath);
}
