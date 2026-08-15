using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexusProgrammer;

internal static partial class MeaAnalyzer
{
    private static readonly string MeaDirectory = Path.Combine(AppContext.BaseDirectory, "MEA");
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
        var meaExecutable = FindMeaExecutable();
        if (meaExecutable is null)
        {
            return MeaAnalysisResult.Fail("MEA executable was not found.");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"nexus-mea-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var biosPath = Path.Combine(tempRoot, "bios.bin");
        await File.WriteAllBytesAsync(biosPath, buffer, cancellationToken);

        try
        {
            var result = await RunMeaAsync(meaExecutable, $"\"{biosPath}\" -skip -exit -json -out \"{tempRoot}\"", tempRoot, cancellationToken);

            var summary = Summarize(result.Output, tempRoot, biosPath);
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

    private static string? FindMeaExecutable()
    {
        if (!Directory.Exists(MeaDirectory))
        {
            return null;
        }

        return Directory.GetFiles(MeaDirectory, "*.exe")
            .OrderByDescending(path => Path.GetFileName(path).Contains("MEA", StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => path)
            .FirstOrDefault();
    }

    private static async Task<(int ExitCode, string Output)> RunMeaAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
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

    private static string Summarize(string output, string outputDirectory, string biosPath)
    {
        var jsonSummary = SummarizeJson(outputDirectory, biosPath);
        if (!string.IsNullOrWhiteSpace(jsonSummary))
        {
            return jsonSummary;
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
        return tableSummary.Length > 0 ? tableSummary : FormatLegacyMeaSummary(lines);
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
                break;
            }

            if (managementEngine is null)
            {
                return string.Empty;
            }

            var engine = managementEngine.Value;
            var summary = new List<string>();
            AddSummaryLine(summary, "Version", JsonValue(engine, "Version"));
            AddSummaryLine(summary, "Type", JsonValue(engine, "Type"));
            AddSummaryLine(summary, "SKU", JsonValue(engine, "SKU"));
            AddSummaryLine(summary, "Family", JsonValue(engine, "Family"));
            AddSummaryLine(summary, "Chipset", JsonValue(engine, "Chipset"));
            AddSummaryLine(summary, "FIT", JsonValue(engine, "Flash Image Tool"));
            AddSummaryLine(summary, "File System", JsonValue(engine, "File System State"));
            return string.Join(Environment.NewLine, summary);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? JsonValue(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
        AddSummaryLine(summary, "Version", version);
        AddSummaryLine(summary, "Type", type);
        AddSummaryLine(summary, "SKU", sku);
        AddSummaryLine(summary, "Family", family);
        AddSummaryLine(summary, "Chipset", cpuGeneration);
        AddSummaryLine(summary, "FIT", fit);
        AddSummaryLine(summary, "File System", fileSystem);

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
        AddSummaryLine(summary, "Version", version);
        AddSummaryLine(summary, "Type", GetField(fields, "Type"));
        AddSummaryLine(summary, "SKU", GetField(fields, "SKU"));
        AddSummaryLine(summary, "Family", GetField(fields, "Family"));
        AddSummaryLine(summary, "Chipset", GetField(fields, "Chipset"));
        AddSummaryLine(summary, "FIT", fit);
        AddSummaryLine(summary, "File System", GetField(fields, "File System State"));
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
            fileSystem ?? string.Empty);
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
    string FileSystem = "");
