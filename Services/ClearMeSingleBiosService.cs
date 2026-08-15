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
        return await ClearAsync(bios, meRegionPath, [fitPath], log: null, cancellationToken);
    }

    public static async Task<ClearMeResult> ClearAsync(
        byte[] bios,
        string meRegionPath,
        IReadOnlyList<string> fitPaths,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(meRegionPath))
        {
            throw new FileNotFoundException("ME Region file was not found.", meRegionPath);
        }

        var candidates = fitPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
        {
            throw new FileNotFoundException("FIT executable was not found.");
        }

        var errors = new List<string>();
        for (var index = 0; index < candidates.Count; index++)
        {
            var fitPath = candidates[index];
            if (!File.Exists(fitPath))
            {
                log?.Invoke($"Clear ME FIT skipped ({index + 1}/{candidates.Count}): {Path.GetFileName(fitPath)} not found");
                errors.Add($"{Path.GetFileName(fitPath)}: FIT executable was not found.");
                continue;
            }

            try
            {
                log?.Invoke($"Clear ME FIT attempt ({index + 1}/{candidates.Count}): {Path.GetFileName(fitPath)}");
                var result = await ClearWithFitAsync(bios, meRegionPath, fitPath, log, cancellationToken);
                log?.Invoke($"Clear ME FIT succeeded: {Path.GetFileName(fitPath)}");
                return result with { Summary = $"FIT used: {Path.GetFileName(fitPath)}{Environment.NewLine}{result.Summary}" };
            }
            catch (Exception ex) when (candidates.Count > 1 && ex is not OperationCanceledException)
            {
                var error = CompactError(ex.Message);
                log?.Invoke($"Clear ME FIT failed: {Path.GetFileName(fitPath)} - {error}");
                errors.Add($"{Path.GetFileName(fitPath)}: {error}");
            }
        }

        throw new InvalidOperationException(
            "Clear ME failed with all FIT candidates:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, errors));
    }

    private static async Task<ClearMeResult> ClearWithFitAsync(
        byte[] bios,
        string meRegionPath,
        string fitPath,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
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
            var usesModularFit = help.Output.Contains("--decompose", StringComparison.OrdinalIgnoreCase);
            var build = await RunFitBuildAsync(usesModularFit, fitPath, inputPath, meRegionCopy, outputPath, tempRoot, cancellationToken);

            var builtPath = File.Exists(outputPath)
                ? outputPath
                : FindBuiltImage(tempRoot, Path.GetDirectoryName(fitPath));
            var failedFileSystem = FailedMeFileSystem(build.Output);
            if (builtPath is null && build.ExitCode != 0 && failedFileSystem is not null)
            {
                log?.Invoke($"Clear ME FIT failed to initialize {failedFileSystem}; repairing ME region input");
                build = await RetryWithRepairedMeFileSystemAsync(
                    usesModularFit,
                    fitPath,
                    inputPath,
                    meRegionCopy,
                    outputPath,
                    tempRoot,
                    failedFileSystem,
                    build.Output,
                    log,
                    cancellationToken);
                builtPath = File.Exists(outputPath)
                    ? outputPath
                    : FindBuiltImage(tempRoot, Path.GetDirectoryName(fitPath));
            }
            if (build.ExitCode != 0 && builtPath is null)
            {
                throw new InvalidOperationException(SummarizeFitError(build.Output));
            }

            if (builtPath is null)
            {
                throw new InvalidOperationException("FIT completed but no output image was found.");
            }

            var builtImage = await File.ReadAllBytesAsync(builtPath, cancellationToken);
            if (builtImage.Length != bios.Length)
            {
                throw new InvalidOperationException(
                    $"FIT output size mismatch: expected {bios.Length} bytes, got {builtImage.Length} bytes ({Path.GetFileName(builtPath)}).");
            }

            return new ClearMeResult(builtImage, SummarizeFitOutput(build.Output));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static Task<FitRunResult> RunFitBuildAsync(
        bool usesModularFit,
        string fitPath,
        string inputPath,
        string? meRegionPath,
        string outputPath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        return usesModularFit
            ? RunModularFitAsync(fitPath, inputPath, meRegionPath, outputPath, workingDirectory, cancellationToken)
            : RunClassicFitAsync(fitPath, inputPath, meRegionPath, outputPath, workingDirectory, cancellationToken);
    }

    private static async Task<FitRunResult> RetryWithRepairedMeFileSystemAsync(
        bool usesModularFit,
        string fitPath,
        string inputPath,
        string meRegionPath,
        string outputPath,
        string workingDirectory,
        string failedFileSystem,
        string firstOutput,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var repairedInput = CreateMeFileSystemRepairedInput(inputPath, meRegionPath, workingDirectory, log);
            log?.Invoke($"Clear ME repair input: ME offset 0x{repairedInput.Region.Offset:X}, size {repairedInput.Region.Size} bytes");
            log?.Invoke($"Clear ME FIT retry after repair: {Path.GetFileName(fitPath)}");
            var retry = await RunFitBuildAsync(
                usesModularFit,
                fitPath,
                repairedInput.Path,
                null,
                outputPath,
                workingDirectory,
                cancellationToken);
            if (retry.ExitCode != 0)
            {
                log?.Invoke($"Clear ME FIT retry after repair failed: {Path.GetFileName(fitPath)} - {FirstLine(SummarizeFitError(retry.Output))}");
            }

            return retry with
            {
                Output = string.Join(Environment.NewLine,
                    firstOutput,
                    $"FIT failed to initialize {failedFileSystem}; retried with repaired ME region input.",
                    $"ME region offset 0x{repairedInput.Region.Offset:X}, size {repairedInput.Region.Size} bytes.",
                    retry.Output)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.Invoke($"Clear ME repair retry skipped: {ex.Message}");
            return new FitRunResult(
                2,
                string.Join(Environment.NewLine,
                    firstOutput,
                    $"ME file system repair retry was skipped: {ex.Message}"));
        }
    }

    private static async Task<FitRunResult> RunClassicFitAsync(
        string fitPath,
        string inputPath,
        string? meRegionPath,
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

        var arguments = string.IsNullOrWhiteSpace(meRegionPath)
            ? $"-b -f \"{configPath}\" -o \"{outputPath}\""
            : $"-b -f \"{configPath}\" -me \"{meRegionPath}\" -o \"{outputPath}\"";
        return await RunFitAsync(fitPath, arguments, workingDirectory, cancellationToken);
    }

    private static async Task<FitRunResult> RunModularFitAsync(
        string fitPath,
        string inputPath,
        string? meRegionPath,
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

        var buildConfigPath = configPath;
        if (!string.IsNullOrWhiteSpace(meRegionPath))
        {
            PatchModularMeRegion(configPath, cleanConfigPath, meRegionPath);
            buildConfigPath = cleanConfigPath;
        }

        return await RunFitAsync(fitPath, $"--loadconfig \"{buildConfigPath}\" --build \"{outputPath}\"", workingDirectory, cancellationToken);
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

    private static RepairedInput CreateMeFileSystemRepairedInput(string inputPath, string meRegionPath, string workingDirectory, Action<string>? log)
    {
        var data = File.ReadAllBytes(inputPath);
        var meRegion = File.ReadAllBytes(meRegionPath);
        var region = IntelFlashDescriptorRegion(data, 2, "ME");
        if (region.Offset + meRegion.Length > data.Length)
        {
            throw new InvalidOperationException(
                $"Selected ME Region exceeds BIOS image bounds ({region.Offset + meRegion.Length} > {data.Length} bytes).");
        }

        if (meRegion.Length > region.Size)
        {
            log?.Invoke($"Selected ME Region is larger than BIOS ME region ({meRegion.Length} > {region.Size} bytes). Writing full ME Region and overwriting following bytes.");
        }

        Array.Fill<byte>(data, 0xFF, region.Offset, region.Size);
        Buffer.BlockCopy(meRegion, 0, data, region.Offset, meRegion.Length);

        var repairedPath = Path.Combine(workingDirectory, "input_me_fs_repaired.bin");
        File.WriteAllBytes(repairedPath, data);
        return new RepairedInput(repairedPath, region);
    }

    private static FlashRegion IntelFlashDescriptorRegion(byte[] buffer, int regionIndex, string name)
    {
        if (buffer.Length < 0x1000)
        {
            throw new InvalidOperationException("Input is too small to contain an Intel Flash Descriptor.");
        }

        ReadOnlySpan<byte> descriptorSignature = [0x5A, 0xA5, 0xF0, 0x0F];
        if (!buffer.AsSpan(0x10, 4).SequenceEqual(descriptorSignature))
        {
            throw new InvalidOperationException("Intel Flash Descriptor signature was not found.");
        }

        var flmap0 = BitConverter.ToInt32(buffer, 0x14);
        var frba = ((flmap0 >> 16) & 0xFF) << 4;
        var entry = frba + regionIndex * 4;
        if (entry + 4 > buffer.Length)
        {
            throw new InvalidOperationException("Intel Flash Descriptor region table is out of range.");
        }

        var value = BitConverter.ToInt32(buffer, entry);
        var baseBlock = value & 0x0FFF;
        var limitBlock = (value >> 16) & 0x0FFF;
        if (baseBlock == 0x0FFF || limitBlock == 0 || limitBlock < baseBlock)
        {
            throw new InvalidOperationException($"Intel Flash Descriptor does not define a valid {name} region.");
        }

        var offset = baseBlock << 12;
        var end = (limitBlock + 1) << 12;
        if (end > buffer.Length)
        {
            throw new InvalidOperationException($"{name} region is outside the input file.");
        }

        return new FlashRegion(name, offset, end - offset);
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
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return new FitRunResult(process.ExitCode, $"{await outputTask}{Environment.NewLine}{await errorTask}");
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw;
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
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
        var success = CleanLines(output).Any(line =>
            line.Contains("built successfully", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Build completed", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Image built", StringComparison.OrdinalIgnoreCase));
        return success ? "FIT build succeeded." : "FIT build completed.";
    }

    private static string SummarizeFitError(string output)
    {
        var failedFileSystem = FailedMeFileSystem(output);
        if (failedFileSystem is not null)
        {
            var messageLines = new List<string>
            {
                $"FIT could not initialize ME {failedFileSystem}.",
                "Auto repair was attempted by replacing the BIOS ME region with the selected ME Region file."
            };
            var repairError = CleanLines(output)
                .LastOrDefault(line => line.StartsWith("ME file system repair retry was skipped:", StringComparison.OrdinalIgnoreCase));
            if (repairError is not null)
            {
                messageLines.Add(repairError);
            }

            return string.Join(Environment.NewLine, messageLines);
        }

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

    private static string? FailedMeFileSystem(string output)
    {
        foreach (var name in new[] { "MFS", "EFS" })
        {
            if (output.Contains($"Failed to initialize {name}", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }

    private static string FirstLine(string text) =>
        text.Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? text;

    private static string CompactError(string text)
    {
        var lines = CleanLines(text).ToArray();
        if (lines.Length == 0)
        {
            return text.Trim();
        }

        var primary = lines[0];
        var importantDetail = lines.FirstOrDefault(line =>
            line.StartsWith("ME file system repair retry was skipped:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("FIT output size mismatch:", StringComparison.OrdinalIgnoreCase));
        return importantDetail is null ? primary : $"{primary} {importantDetail}";
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

    private sealed record FlashRegion(string Name, int Offset, int Size);

    private sealed record RepairedInput(string Path, FlashRegion Region);
}

internal sealed record ClearMeResult(byte[] Bios, string Summary);
