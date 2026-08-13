using System.Diagnostics;
using System.IO;
using System.Text;
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
            var result = await RunMeaAsync(meaExecutable, $"-q -f \"{biosPath}\"", tempRoot, cancellationToken);

            return result.ExitCode == 0
                ? MeaAnalysisResult.Ok(Summarize(result.Output))
                : MeaAnalysisResult.Fail(Summarize(result.Output));
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

    private static string Summarize(string output)
    {
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

        return FormatMeaSummary(lines);
    }

    private static string FormatMeaSummary(IReadOnlyList<string> lines)
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

    private static string? LastValue(IEnumerable<string> lines, string key)
    {
        var prefix = $"{key}:";
        return lines
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
}

internal sealed record MeaAnalysisResult(bool Success, string Summary)
{
    public static MeaAnalysisResult Ok(string summary) => new(true, summary);

    public static MeaAnalysisResult Fail(string summary) => new(false, summary);
}
