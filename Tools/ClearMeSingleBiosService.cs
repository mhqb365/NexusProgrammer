using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace NexusProgrammer;

internal static class ClearMeSingleBiosService
{
    public static async Task<ClearMeResult> ClearAsync(
        byte[] bios,
        string meRegionPath,
        string fitPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(meRegionPath))
        {
            throw new FileNotFoundException("ME Region file was not found.", meRegionPath);
        }

        if (!File.Exists(fitPath))
        {
            throw new FileNotFoundException("FIT executable was not found.", fitPath);
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"nexus-clearme-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input_original.bin");
        var meRegionCopy = Path.Combine(tempRoot, "ME Region.bin");
        var outputPath = Path.Combine(tempRoot, "outimage.bin");
        await File.WriteAllBytesAsync(inputPath, bios, cancellationToken);
        File.Copy(meRegionPath, meRegionCopy, overwrite: true);

        try
        {
            var help = await RunFitAsync(fitPath, "-?", tempRoot, cancellationToken);
            var build = help.Output.Contains("--decompose", StringComparison.OrdinalIgnoreCase)
                ? await RunModularFitAsync(fitPath, inputPath, meRegionCopy, outputPath, tempRoot, cancellationToken)
                : await RunClassicFitAsync(fitPath, inputPath, meRegionCopy, outputPath, tempRoot, cancellationToken);

            var builtPath = File.Exists(outputPath)
                ? outputPath
                : FindBuiltImage(tempRoot, Path.GetDirectoryName(fitPath));
            if (build.ExitCode != 0 && builtPath is null)
            {
                throw new InvalidOperationException(SummarizeFitError(build.Output));
            }

            if (builtPath is null)
            {
                throw new InvalidOperationException("FIT completed but no output image was found.");
            }

            return new ClearMeResult(await File.ReadAllBytesAsync(builtPath, cancellationToken), SummarizeFitOutput(build.Output));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task<FitRunResult> RunClassicFitAsync(
        string fitPath,
        string inputPath,
        string meRegionPath,
        string outputPath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(workingDirectory, "config.xml");
        var save = await RunFitAsync(fitPath, $"-f \"{inputPath}\" -save \"{configPath}\"", workingDirectory, cancellationToken);
        if (save.ExitCode != 0 || !File.Exists(configPath))
        {
            return save;
        }

        return await RunFitAsync(
            fitPath,
            $"-b -f \"{configPath}\" -me \"{meRegionPath}\" -o \"{outputPath}\"",
            workingDirectory,
            cancellationToken);
    }

    private static async Task<FitRunResult> RunModularFitAsync(
        string fitPath,
        string inputPath,
        string meRegionPath,
        string outputPath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(workingDirectory, "config.xml");
        var cleanConfigPath = Path.Combine(workingDirectory, "config_clean.xml");
        var decompose = await RunFitAsync(
            fitPath,
            $"--decompose \"{inputPath}\" --saveconfig \"{configPath}\"",
            workingDirectory,
            cancellationToken);
        if (decompose.ExitCode != 0 || !File.Exists(configPath))
        {
            return decompose;
        }

        PatchModularMeRegion(configPath, cleanConfigPath, meRegionPath);
        return await RunFitAsync(fitPath, $"--loadconfig \"{cleanConfigPath}\" --build \"{outputPath}\"", workingDirectory, cancellationToken);
    }

    private static void PatchModularMeRegion(string configPath, string outputPath, string meRegionPath)
    {
        var document = XDocument.Load(configPath);
        var target = document.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "MeRegionFile" ||
                string.Equals((string?)element.Attribute("key"), "CsePlugin:CseRegion:MeRegionFile", StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            throw new InvalidOperationException("FIT modular XML does not contain ME Region file setting.");
        }

        target.SetAttributeValue("value", meRegionPath);
        document.Save(outputPath);
    }

    private static async Task<FitRunResult> RunFitAsync(string fitPath, string arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fitPath,
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
        return new FitRunResult(process.ExitCode, $"{await outputTask}{Environment.NewLine}{await errorTask}");
    }

    private static string? FindBuiltImage(string workingDirectory, string? fitDirectory)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "outimage.bin",
            "outimage.bin.bin",
            "intermediate.bin"
        };
        var roots = new[] { workingDirectory, fitDirectory }
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Cast<string>();

        return roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .FirstOrDefault(path => names.Contains(Path.GetFileName(path)));
    }

    private static string SummarizeFitOutput(string output)
    {
        var lines = CleanLines(output)
            .Where(line =>
                line.Contains("FIT version used", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("MFIT version used", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Build completed", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Image built", StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToArray();
        return lines.Length > 0 ? string.Join(Environment.NewLine, lines) : "FIT build completed.";
    }

    private static string SummarizeFitError(string output)
    {
        var lines = CleanLines(output)
            .Where(line =>
                line.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Invalid", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Details:", StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToArray();
        return lines.Length > 0 ? string.Join(Environment.NewLine, lines) : "FIT build failed.";
    }

    private static IEnumerable<string> CleanLines(string output) =>
        output.Replace("\r", string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);

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

    private sealed record FitRunResult(int ExitCode, string Output);
}

internal sealed record ClearMeResult(byte[] Bios, string Summary);
