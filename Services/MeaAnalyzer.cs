using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexusProgrammer;

internal static partial class MeaAnalyzer
{
    private const string MeAnalyzerDirectoryName = "MEAnalyzer";
    private static readonly string MeAnalyzerDirectory = Path.Combine(AppContext.BaseDirectory, MeAnalyzerDirectoryName);
    private static readonly byte[] IntelFlashDescriptorSignature = [0x5A, 0xA5, 0xF0, 0x0F];
    private static readonly byte[][] IntelFirmwareMarkers =
    [
        Encoding.ASCII.GetBytes("$FPT"),
        Encoding.ASCII.GetBytes("IFWI"),
        Encoding.ASCII.GetBytes("CSME"),
        Encoding.ASCII.GetBytes("Intel(R) ME")
    ];

    public static async Task<MeaAnalysisResult> AnalyzeAsync(byte[] buffer, CancellationToken cancellationToken = default)
    {
        var meaTool = FindMeaTool();
        if (meaTool is null)
        {
            return MeaAnalysisResult.Fail("MEAnalyzer executable was not found. Build MEAnalyzer\\MEA.py to MEA.exe first.");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"nexus-mea-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var biosPath = Path.Combine(tempRoot, "bios.bin");
        await File.WriteAllBytesAsync(biosPath, buffer, cancellationToken);

        try
        {
            var result = await RunMeaAsync(meaTool, biosPath, tempRoot, cancellationToken);

            var summary = Summarize(result.Output, tempRoot, biosPath, buffer);
            var info = ParseInfo(summary);
            return result.ExitCode == 0
                ? MeaAnalysisResult.Ok(summary, info)
                : MeaAnalysisResult.Fail(summary, info);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    public static bool IsLikelyIntelFirmware(byte[] buffer)
    {
        if (buffer.Length >= 0x14 && buffer.AsSpan(0x10, 4).SequenceEqual(IntelFlashDescriptorSignature))
        {
            return true;
        }

        var searchLength = Math.Min(buffer.Length, 0x200000);
        var searchArea = buffer.AsSpan(0, searchLength);
        foreach (var marker in IntelFirmwareMarkers)
        {
            if (searchArea.IndexOf(marker) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindMeaTool()
    {
        if (!Directory.Exists(MeAnalyzerDirectory))
        {
            return null;
        }

        var executablePath = Path.Combine(MeAnalyzerDirectory, "MEA.exe");
        return File.Exists(executablePath) ? executablePath : null;
    }

    private static async Task<(int ExitCode, string Output)> RunMeaAsync(string executablePath, string biosPath, string outputDirectory, CancellationToken cancellationToken)
    {
        var arguments = $"\"{biosPath}\" -skip -exit -json -out \"{outputDirectory}\"";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = MeAnalyzerDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, $"{await outputTask}{Environment.NewLine}{await errorTask}");
    }

    private static string Summarize(string output, string outputDirectory, string biosPath, byte[] buffer)
    {
        var jsonSummary = SummarizeJson(outputDirectory, biosPath);
        if (!string.IsNullOrWhiteSpace(jsonSummary))
        {
            return AddBiosIdentity(jsonSummary, buffer);
        }

        output = AnsiRegex().Replace(output, string.Empty).Replace("\r", string.Empty);
        var lines = output.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Where(line => !line.StartsWith("╔", StringComparison.Ordinal) &&
                           !line.StartsWith("║", StringComparison.Ordinal) &&
                           !line.StartsWith("╚", StringComparison.Ordinal) &&
                           !line.Contains("Welcome to Intel Engine", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (lines.Length == 0)
        {
            return "MEA completed with no console output.";
        }

        var tableSummary = FormatPlatoMeaSummary(lines);
        var meaSummary = tableSummary.Length > 0 ? tableSummary : FormatLegacyMeaSummary(lines);
        return AddBiosIdentity(meaSummary, buffer);
    }

    private static string SummarizeJson(string outputDirectory, string biosPath)
    {
        try
        {
            var jsonPath = Directory.GetFiles(outputDirectory, "*.json", SearchOption.AllDirectories)
                .OrderByDescending(path => string.Equals(Path.GetFileNameWithoutExtension(path), Path.GetFileName(biosPath), StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (jsonPath is null)
            {
                return string.Empty;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            JsonElement? managementEngine = null;
            JsonElement? firmware = null;
            foreach (var fileProperty in document.RootElement.EnumerateObject())
            {
                if (fileProperty.Value.ValueKind != JsonValueKind.Object ||
                    !fileProperty.Value.TryGetProperty("Management Engine", out var engineArray) ||
                    engineArray.ValueKind != JsonValueKind.Array ||
                    engineArray.GetArrayLength() == 0)
                {
                    continue;
                }

                managementEngine = engineArray[0];
                firmware = fileProperty.Value;
                break;
            }

            if (managementEngine is null)
            {
                return string.Empty;
            }

            var engine = managementEngine.Value;
            var summary = new List<string>();
            AddSummaryLine(summary, "Family", JsonValue(engine, "Family"));
            AddSummaryLine(summary, "Version", JsonValue(engine, "Version"));
            AddSummaryLine(summary, "Release", JsonValue(engine, "Release"));
            AddSummaryLine(summary, "Type", JsonValue(engine, "Type"));
            AddSummaryLine(summary, "SKU", JsonValue(engine, "SKU"));
            AddSummaryLine(summary, "Chipset", JsonValue(engine, "Chipset"));
            AddSummaryLine(summary, "Chipset Support", JsonValue(engine, "Chipset Support") ?? FindFirstJsonValue(firmware, "Chipset Support"));
            AddSummaryLine(summary, "TCB SVN", JsonValue(engine, "TCB SVN") ?? JsonValue(engine, "TCB Security Version Number"));
            AddSummaryLine(summary, "VCN", JsonValue(engine, "VCN") ?? JsonValue(engine, "Version Control Number"));
            AddSummaryLine(summary, "Production Ready", JsonValue(engine, "Production Ready"));
            AddSummaryLine(summary, "Workstation Support", JsonValue(engine, "Workstation Support"));
            AddSummaryLine(summary, "OEM Configuration", JsonValue(engine, "OEM Configuration"));
            AddSummaryLine(summary, "Date", JsonValue(engine, "Date"));
            AddSummaryLine(summary, "Size", FormatMeaSize(JsonValue(engine, "Size")));
            AddSummaryLine(summary, "FIT", JsonValue(engine, "Flash Image Tool"));
            AddSummaryLine(summary, "File System", JsonValue(engine, "File System State"));
            AddSummaryLine(summary, "MEA Database Name", JsonValue(engine, "MEA Database Name"));
            AddSummaryLine(summary, "MEA Support Status", JsonValue(engine, "MEA Support Status"));
            AddSummaryLine(summary, "RSA Signature Hash", JsonValue(engine, "RSA Signature Hash"));
            return string.Join(Environment.NewLine, summary);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? JsonValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? FindFirstJsonValue(JsonElement? element, string propertyName)
    {
        if (element is null)
        {
            return null;
        }

        foreach (var property in element.Value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in property.Value.EnumerateArray())
            {
                var value = JsonValue(item, propertyName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string FormatLegacyMeaSummary(IReadOnlyList<string> lines)
    {
        var version = LastValue(lines, "CSME Version") ?? LastValue(lines, "ME Version");
        var fit = LastValue(lines, "FITC Version");
        var fileSystem = LastValue(lines, "File System Stage");
        var sku = LastValue(lines, "SKU");
        var family = lines.Any(line => line.StartsWith("CSME ", StringComparison.OrdinalIgnoreCase))
            ? "CSE ME"
            : "ME";
        var fullVersion = LastValue(lines, "CSME Full Version") ?? LastValue(lines, "ME Full Version");
        var type = InferMeaType(fullVersion);
        var cpuGeneration = LastValue(lines, "CPU Generation");

        var summary = new List<string>();
        AddSummaryLine(summary, "Family", family);
        AddSummaryLine(summary, "Version", version);
        AddSummaryLine(summary, "Release", LastValue(lines, "Release"));
        AddSummaryLine(summary, "Type", type);
        AddSummaryLine(summary, "SKU", sku);
        AddSummaryLine(summary, "Chipset", cpuGeneration);
        AddSummaryLine(summary, "Chipset Support", LastValue(lines, "Chipset Support"));
        AddSummaryLine(summary, "TCB SVN", LastValue(lines, "TCB SVN") ?? LastValue(lines, "TCB Security Version Number"));
        AddSummaryLine(summary, "VCN", LastValue(lines, "VCN") ?? LastValue(lines, "Version Control Number"));
        AddSummaryLine(summary, "Production Ready", LastValue(lines, "Production Ready"));
        AddSummaryLine(summary, "Workstation Support", LastValue(lines, "Workstation Support"));
        AddSummaryLine(summary, "OEM Configuration", LastValue(lines, "OEM Configuration"));
        AddSummaryLine(summary, "Date", LastValue(lines, "Date"));
        AddSummaryLine(summary, "Size", FormatMeaSize(LastValue(lines, "Size")));
        AddSummaryLine(summary, "FIT", fit);
        AddSummaryLine(summary, "File System", fileSystem);
        AddSummaryLine(summary, "MEA Database Name", LastValue(lines, "MEA Database Name"));
        AddSummaryLine(summary, "MEA Support Status", LastValue(lines, "MEA Support Status"));
        AddSummaryLine(summary, "RSA Signature Hash", LastValue(lines, "RSA Signature Hash"));

        return string.Join(Environment.NewLine, summary);
    }

    private static string FormatPlatoMeaSummary(IReadOnlyList<string> lines)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var insideFirstTable = false;
        var sawDataRow = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("╔", StringComparison.Ordinal))
            {
                if (sawDataRow)
                {
                    break;
                }

                insideFirstTable = true;
                continue;
            }

            if (!insideFirstTable)
            {
                continue;
            }

            if (line.StartsWith("╚", StringComparison.Ordinal))
            {
                if (sawDataRow)
                {
                    break;
                }

                insideFirstTable = false;
                continue;
            }

            if (!line.Contains('│'))
            {
                continue;
            }

            var cells = line.Trim('║', ' ').Split('│')
                .Select(cell => cell.Trim())
                .Where(cell => cell.Length > 0)
                .ToArray();
            if (cells.Length < 2)
            {
                continue;
            }

            sawDataRow = true;
            fields.TryAdd(cells[0], cells[^1]);
        }

        var version = GetField(fields, "Version");
        var fit = GetField(fields, "Flash Image Tool");
        if (string.IsNullOrWhiteSpace(version) && string.IsNullOrWhiteSpace(fit))
        {
            return string.Empty;
        }

        var summary = new List<string>();
        AddSummaryLine(summary, "Family", GetField(fields, "Family"));
        AddSummaryLine(summary, "Version", version);
        AddSummaryLine(summary, "Release", GetField(fields, "Release"));
        AddSummaryLine(summary, "Type", GetField(fields, "Type"));
        AddSummaryLine(summary, "SKU", GetField(fields, "SKU"));
        AddSummaryLine(summary, "Chipset", GetField(fields, "Chipset"));
        AddSummaryLine(summary, "Chipset Support", GetField(fields, "Chipset Support"));
        AddSummaryLine(summary, "TCB SVN", GetField(fields, "TCB SVN") ?? GetField(fields, "TCB Security Version Number"));
        AddSummaryLine(summary, "VCN", GetField(fields, "VCN") ?? GetField(fields, "Version Control Number"));
        AddSummaryLine(summary, "Production Ready", GetField(fields, "Production Ready"));
        AddSummaryLine(summary, "Workstation Support", GetField(fields, "Workstation Support"));
        AddSummaryLine(summary, "OEM Configuration", GetField(fields, "OEM Configuration"));
        AddSummaryLine(summary, "Date", GetField(fields, "Date"));
        AddSummaryLine(summary, "Size", FormatMeaSize(GetField(fields, "Size")));
        AddSummaryLine(summary, "FIT", fit);
        AddSummaryLine(summary, "File System", GetField(fields, "File System State"));
        AddSummaryLine(summary, "MEA Database Name", GetField(fields, "MEA Database Name"));
        AddSummaryLine(summary, "MEA Support Status", GetField(fields, "MEA Support Status"));
        AddSummaryLine(summary, "RSA Signature Hash", GetField(fields, "RSA Signature Hash"));
        return string.Join(Environment.NewLine, summary);
    }

    private static string? GetField(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : null;

    private static string? LastValue(IEnumerable<string> lines, string key)
    {
        var prefix = $"{key}:";
        return lines
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(line => line[prefix.Length..].Trim())
            .LastOrDefault(value => value.Length > 0 && !value.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    private static string? InferMeaType(string? fullVersion)
    {
        if (string.IsNullOrWhiteSpace(fullVersion))
        {
            return null;
        }

        return fullVersion.Contains("_EXTR", StringComparison.OrdinalIgnoreCase)
            ? "Extracted"
            : "Region";
    }

    private static string AddBiosIdentity(string meaSummary, byte[] buffer)
    {
        var (vendor, version) = DetectBiosIdentity(buffer);
        var summary = new List<string>
        {
            $"  BIOS Vendor: {(string.IsNullOrWhiteSpace(vendor) ? "unknown" : vendor)}",
            $"  BIOS Version: {(string.IsNullOrWhiteSpace(version) ? "Not detected" : version)}"
        };
        if (!string.IsNullOrWhiteSpace(meaSummary))
        {
            summary.Add(meaSummary);
        }

        return string.Join(Environment.NewLine, summary);
    }

    private static (string Vendor, string Version) DetectBiosIdentity(byte[] buffer)
    {
        var strings = FirmwareStrings(buffer);
        var allText = string.Join('\n', strings.Select(item => item.Value)).ToLowerInvariant();
        var vendor = VendorMarkers.FirstOrDefault(item => item.Markers.Any(allText.Contains)).Vendor ?? string.Empty;
        if (vendor.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        var pattern = BiosVersionPatterns[vendor];
        for (var index = 0; index < strings.Count; index++)
        {
            var item = strings[index];
            if (!BiosVersionLabelRegex().IsMatch(item.Value))
            {
                continue;
            }

            var candidates = new List<string> { CleanBiosVersionCandidate(item.Value) };
            candidates.AddRange(strings.Skip(index + 1).Take(6)
                .Where(next => next.Offset - item.Offset <= 512)
                .Select(next => next.Value));
            var match = candidates.Select(candidate => pattern.Match(candidate.Trim())).FirstOrDefault(candidate => candidate.Success);
            if (match?.Success == true)
            {
                return (vendor, match.Value);
            }
        }

        if (vendor is "Lenovo" or "HP")
        {
            var match = strings.Select(item => pattern.Match(item.Value.Trim())).FirstOrDefault(candidate => candidate.Success);
            if (match?.Success == true)
            {
                return (vendor, match.Value);
            }
        }

        return (vendor, string.Empty);
    }

    private static List<(int Offset, string Value)> FirmwareStrings(byte[] buffer)
    {
        var values = new List<(int Offset, string Value)>();
        for (var offset = 0; offset < buffer.Length;)
        {
            if (buffer[offset] is >= 0x20 and <= 0x7E)
            {
                var start = offset;
                while (offset < buffer.Length && buffer[offset] is >= 0x20 and <= 0x7E && offset - start < 128)
                {
                    offset++;
                }
                if (offset - start >= 3)
                {
                    values.Add((start, Encoding.ASCII.GetString(buffer, start, offset - start)));
                }
            }
            else
            {
                offset++;
            }
        }

        for (var alignment = 0; alignment < 2; alignment++)
        {
            for (var offset = alignment; offset + 1 < buffer.Length;)
            {
                if (buffer[offset] is >= 0x20 and <= 0x7E && buffer[offset + 1] == 0)
                {
                    var start = offset;
                    var chars = new StringBuilder();
                    while (offset + 1 < buffer.Length && buffer[offset] is >= 0x20 and <= 0x7E && buffer[offset + 1] == 0 && chars.Length < 128)
                    {
                        chars.Append((char)buffer[offset]);
                        offset += 2;
                    }
                    if (chars.Length >= 3)
                    {
                        values.Add((start, chars.ToString()));
                    }
                }
                else
                {
                    offset += 2;
                }
            }
        }

        return values.OrderBy(item => item.Offset).Distinct().ToList();
    }

    private static string CleanBiosVersionCandidate(string value) =>
        BiosVersionPrefixRegex().Replace(value, string.Empty).Trim(' ', '\t', '\r', '\n', '\0', ':', ';', ',', '-', '_', '[', ']', '{', '}');

    private static string? FormatMeaSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var normalized = value.Trim();
        if (MeaSizeWithUnitRegex().IsMatch(normalized))
        {
            return normalized;
        }

        try
        {
            var bytes = normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt64(normalized[2..], 16)
                : Convert.ToInt64(normalized);
            return $"{bytes / (1024d * 1024d):0.00} MB";
        }
        catch
        {
            return normalized;
        }
    }

    private static readonly (string Vendor, string[] Markers)[] VendorMarkers =
    [
        ("Dell", ["dell inc", "dell computer", "optiplex", "latitude", "vostro", "inspiron"]),
        ("Lenovo", ["lenovo", "thinkpad", "thinkcentre"]),
        ("HP", ["hewlett-packard", "hewlett packard", "elitebook", "probook", "zbook"]),
        ("Acer", ["acer incorporated", "acer inc", "aspire", "travelmate"]),
        ("ASUS", ["asustek", "asus computer"])
    ];

    private static readonly Dictionary<string, Regex> BiosVersionPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dell"] = new Regex(@"^(?:A\d{2}|\d{1,2}\.\d{1,2}\.\d{1,3})$", RegexOptions.IgnoreCase),
        ["Lenovo"] = new Regex(@"^[A-Z0-9]{3,5}ET\d{2}W(?:\s*\([^)]+\))?$", RegexOptions.IgnoreCase),
        ["HP"] = new Regex(@"^(?:[A-Z0-9]{1,4}\s+Ver\.\s+[A-Z0-9.]+|F\.\d{2}(?:\.\d{2})?)$", RegexOptions.IgnoreCase),
        ["Acer"] = new Regex(@"^V\d+\.\d+(?:\.\d+)?$", RegexOptions.IgnoreCase),
        ["ASUS"] = new Regex(@"^(?:[A-Z][A-Z0-9-]{2,20}(?:AS)?\.)?\d{3,4}$", RegexOptions.IgnoreCase)
    };

    private static MeaFirmwareInfo ParseInfo(string summary)
    {
        string? value(string key) => LastValue(summary.Split(Environment.NewLine), key);
        var version = value("Version");
        var fit = value("FIT");
        var sku = value("SKU");
        var type = value("Type");
        var family = value("Family");
        var chipset = value("Chipset");
        var fileSystem = value("File System");
        var versionParts = VersionParts(version);
        return new MeaFirmwareInfo(
            version ?? string.Empty,
            versionParts.Major,
            versionParts.Minor,
            family ?? string.Empty,
            sku ?? string.Empty,
            type ?? string.Empty,
            chipset ?? string.Empty,
            fit ?? string.Empty,
            fileSystem ?? string.Empty,
            value("Release") ?? string.Empty,
            value("TCB SVN") ?? string.Empty,
            value("VCN") ?? string.Empty,
            value("Production Ready") ?? string.Empty,
            value("Workstation Support") ?? string.Empty,
            value("OEM Configuration") ?? string.Empty,
            value("Date") ?? string.Empty,
            value("Size") ?? string.Empty,
            value("Chipset Support") ?? string.Empty,
            value("MEA Database Name") ?? string.Empty,
            value("MEA Support Status") ?? string.Empty,
            value("RSA Signature Hash") ?? string.Empty,
            value("BIOS Vendor") ?? string.Empty,
            value("BIOS Version") ?? string.Empty);
    }

    internal static (int Major, int Minor, int Hotfix, int Build) VersionParts(string? value)
    {
        var match = VersionRegex().Match(value ?? string.Empty);
        if (!match.Success)
        {
            return (0, 0, 0, 0);
        }

        return (
            ParseInt(match.Groups["major"].Value),
            ParseInt(match.Groups["minor"].Value),
            ParseInt(match.Groups["hotfix"].Value),
            ParseInt(match.Groups["build"].Value));
    }

    internal static long VersionRank(string? value)
    {
        var version = VersionParts(value);
        return version.Major * 1_000_000_000L + version.Minor * 1_000_000L + version.Hotfix * 10_000L + version.Build;
    }

    private static int ParseInt(string value) =>
        int.TryParse(value, out var parsed) ? parsed : 0;

    private static void AddSummaryLine(ICollection<string> summary, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            summary.Add($"  {label}: {value}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*[A-Za-z]")]
    private static partial Regex AnsiRegex();

    [GeneratedRegex(@"(?<major>\d+)\.(?<minor>\d+)(?:\.(?<hotfix>\d+))?(?:\.(?<build>\d+))?")]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"(?i)\bbios\s*(?:version|revision|id)\b")]
    private static partial Regex BiosVersionLabelRegex();

    [GeneratedRegex(@"(?i)^.*?bios\s*(?:version|revision|id)\s*[:=\-]?\s*")]
    private static partial Regex BiosVersionPrefixRegex();

    [GeneratedRegex(@"(?i)\s(?:bytes?|[kmgt](?:i)?b)$")]
    private static partial Regex MeaSizeWithUnitRegex();
}

internal sealed record MeaAnalysisResult(bool Success, string Summary, MeaFirmwareInfo Info)
{
    public static MeaAnalysisResult Ok(string summary, MeaFirmwareInfo info) => new(true, summary, info);

    public static MeaAnalysisResult Fail(string summary, MeaFirmwareInfo? info = null) =>
        new(false, summary, info ?? new MeaFirmwareInfo());
}

internal sealed record MeaFirmwareInfo(
    string Version = "",
    int Major = 0,
    int Minor = 0,
    string Family = "",
    string Sku = "",
    string Type = "",
    string Chipset = "",
    string Fit = "",
    string FileSystem = "",
    string Release = "",
    string TcbSvn = "",
    string Vcn = "",
    string ProductionReady = "",
    string WorkstationSupport = "",
    string OemConfiguration = "",
    string Date = "",
    string Size = "",
    string ChipsetSupport = "",
    string MeaDatabaseName = "",
    string MeaSupportStatus = "",
    string RsaSignatureHash = "",
    string BiosVendor = "",
    string BiosVersion = "");
