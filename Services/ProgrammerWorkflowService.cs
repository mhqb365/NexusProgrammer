using System.Diagnostics;

namespace NexusProgrammer;

public sealed record ProgrammerSelection(string Key, string StatusText, bool IsConnected)
{
    public IChipProgrammer CreateProgrammer() => Key switch
    {
        "ch341" => new ChNativeProgrammer(),
        "ch347" => new Ch347NativeProgrammer(),
        "rt809f" => new RT809FSDKProgrammer(),
        "rt809h" => new RT809HSDKProgrammer(),
        "t48" => new T48SDKProgrammer(),
        _ => new MockProgrammer()
    };
}

public sealed record ProgrammerScriptResult(bool SaveAfterScript);

public static class ProgrammerWorkflowService
{
    public static ProgrammerSelection ResolveSelection(string selectedMode, ProgrammerDetection detection)
    {
        selectedMode = string.IsNullOrWhiteSpace(selectedMode) ? "auto" : selectedMode;
        if (selectedMode == "auto")
        {
            if (detection.Ch341Detected)
            {
                return Connected("ch341");
            }

            if (detection.Ch347Detected)
            {
                return Connected("ch347");
            }

            if (detection.Rt809fDetected)
            {
                return Connected("rt809f");
            }

            if (detection.Rt809hDetected)
            {
                return Connected("rt809h");
            }

            if (detection.T48Detected)
            {
                return Connected("t48");
            }

            return new ProgrammerSelection("none", "Programmer disconnected", IsConnected: false);
        }

        return detection.IsConnected(selectedMode)
            ? Connected(selectedMode)
            : new ProgrammerSelection("none", $"{DisplayName(selectedMode)} disconnected", IsConnected: false);
    }

    public static string DisplayName(string key) => key switch
    {
        "ch341" => "CH341",
        "ch347" => "CH347",
        "rt809f" => "RT809F",
        "rt809h" => "RT809H",
        "t48" => "XGecu T48",
        _ => "Programmer"
    };

    public static string FormatId(byte[] id) => string.Join(" ", id.Select(x => x.ToString("X2")));

    public static bool IsInvalidJedecId(byte[] id)
    {
        if (id.Length == 0)
        {
            return true;
        }

        if (id.All(value => value == 0x00) || id.All(value => value == 0xFF))
        {
            return true;
        }

        return id.Length >= 3 && id[0] == 0x03 && id[1] == 0x00 && id[2] == 0x00;
    }

    public static string FormatBytes(int bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024.0):0.##} MB";
        }

        return bytes >= 1024 ? $"{bytes / 1024.0:0.##} KB" : $"{bytes} B";
    }

    public static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m {elapsed.Seconds:D2}.{elapsed.Milliseconds / 100}s";
        }

        if (elapsed.TotalMinutes >= 1)
        {
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:D2}.{elapsed.Milliseconds / 100}s";
        }

        return $"{elapsed.TotalSeconds:0.0}s";
    }

    public static string FormatSpeed(int bytes, TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds <= 0)
        {
            return "n/a";
        }

        var bytesPerSecond = bytes / elapsed.TotalSeconds;
        return bytesPerSecond >= 1024 * 1024
            ? $"{bytesPerSecond / (1024 * 1024):0.##} MB/s"
            : $"{bytesPerSecond / 1024:0.##} KB/s";
    }

    public static string FirstLogLine(string message) =>
        message.Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? message;

    public static bool Requires1V8Adapter(ChipProfile chip)
    {
        var volts = chip.Volts.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
        return volts.Contains("1.8", StringComparison.OrdinalIgnoreCase) ||
               volts.Contains("1V8", StringComparison.OrdinalIgnoreCase);
    }

    public static bool SameVoltageProfile(ChipProfile left, ChipProfile right) =>
        string.Equals(NormalizeVoltage(left.Volts), NormalizeVoltage(right.Volts), StringComparison.OrdinalIgnoreCase);

    public static bool ChipMatchesId(ChipProfile chip, byte[] id, IEnumerable<IcCandidate> catalog)
    {
        var idText = FormatId(id);
        return catalog.Any(candidate =>
            string.Equals(candidate.Profile.Name, chip.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.JedecId, idText, StringComparison.OrdinalIgnoreCase));
    }

    public static int ParseStartAddress(string text)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value) ? value : 0;
    }

    public static async Task<byte[]> ReadChipAsync(
        IChipProgrammer programmer,
        ChipProfile chip,
        int startAddress,
        int length,
        IProgress<int> progress,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        log($"Read request: {FormatBytes(length)} from 0x{startAddress:X6}");
        return await programmer.ReadAsync(chip, startAddress, length, progress, cancellationToken);
    }

    public static async Task WriteChipAsync(
        IChipProgrammer programmer,
        ChipProfile chip,
        int startAddress,
        byte[] buffer,
        bool skipBlankPages,
        bool unprotectFirst,
        IProgress<int> progress,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        log($"Write request: {FormatBytes(buffer.Length)} to 0x{startAddress:X6}{(skipBlankPages ? " (skip FF pages)" : "")}, voltage profile {chip.Volts}");
        await UnprotectIfRequestedAsync(programmer, chip, unprotectFirst, progress, log, cancellationToken);
        await programmer.WriteAsync(chip, startAddress, buffer, progress, skipBlankPages, cancellationToken);
    }

    public static async Task<bool> VerifyChipAsync(
        IChipProgrammer programmer,
        ChipProfile chip,
        int startAddress,
        byte[] buffer,
        IProgress<int> progress,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        log($"Verify request: {FormatBytes(buffer.Length)} at 0x{startAddress:X6}");
        var ok = await programmer.VerifyAsync(chip, startAddress, buffer, progress, cancellationToken);
        log(ok ? "Verify OK" : "Verify failed");
        return ok;
    }

    public static async Task EraseChipAsync(
        IChipProgrammer programmer,
        ChipProfile chip,
        bool unprotectFirst,
        IProgress<int> progress,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        await UnprotectIfRequestedAsync(programmer, chip, unprotectFirst, progress, log, cancellationToken);
        await programmer.EraseAsync(chip, progress, cancellationToken);
    }

    public static async Task<ProgrammerScriptResult> RunScriptAsync(
        string script,
        IChipProgrammer programmer,
        ChipProfile chip,
        byte[] buffer,
        int startAddress,
        bool skipBlankPages,
        bool unprotectFirst,
        IProgress<int> progress,
        Action<string> log,
        Action<byte[]> applyReadBuffer,
        CancellationToken cancellationToken)
    {
        if (string.Equals(script, "Read + verify", StringComparison.OrdinalIgnoreCase))
        {
            await RunReadVerifyScriptAsync(programmer, chip, buffer.Length, startAddress, progress, log, applyReadBuffer, cancellationToken);
            return new ProgrammerScriptResult(SaveAfterScript: true);
        }

        await RunEraseWriteVerifyScriptAsync(programmer, chip, buffer, startAddress, skipBlankPages, unprotectFirst, progress, log, cancellationToken);
        return new ProgrammerScriptResult(SaveAfterScript: false);
    }

    private static ProgrammerSelection Connected(string key) =>
        new(key, $"{DisplayName(key)} connected", IsConnected: true);

    private static string NormalizeVoltage(string volts) =>
        volts.Replace(" ", "", StringComparison.OrdinalIgnoreCase).TrimEnd('V');

    private static async Task UnprotectIfRequestedAsync(
        IChipProgrammer programmer,
        ChipProfile chip,
        bool unprotectFirst,
        IProgress<int> progress,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (!unprotectFirst)
        {
            return;
        }

        log($"Unprotect request: {chip.Name}");
        await programmer.UnprotectAsync(chip, progress, cancellationToken);
        log("Unprotect completed");
    }

    private static async Task RunReadVerifyScriptAsync(
        IChipProgrammer programmer,
        ChipProfile chip,
        int length,
        int startAddress,
        IProgress<int> progress,
        Action<string> log,
        Action<byte[]> applyReadBuffer,
        CancellationToken cancellationToken)
    {
        log($"Script request: read and verify {FormatBytes(length)} from 0x{startAddress:X6}");
        log("Script stage: read started");
        TimeSpan readElapsed;
        TimeSpan verifyElapsed;
        bool readOk;
        byte[] readBuffer;
        if (programmer is RT809HSDKProgrammer rt809hProgrammer)
        {
            var result = await rt809hProgrammer.ReadAndVerifyAsync(
                chip,
                startAddress,
                length,
                progress,
                progress,
                (data, elapsed) =>
                {
                    readBuffer = data;
                    applyReadBuffer(data);
                    readElapsed = elapsed;
                    log($"Script stage: read completed: {FormatBytes(data.Length)} in {FormatDuration(readElapsed)} ({FormatSpeed(data.Length, readElapsed)})");
                },
                () => log("Script stage: verify started"),
                cancellationToken);
            readElapsed = result.ReadElapsed;
            verifyElapsed = result.VerifyElapsed;
            readOk = result.Verified;
        }
        else
        {
            var stageWatch = Stopwatch.StartNew();
            readBuffer = await programmer.ReadAsync(chip, startAddress, length, progress, cancellationToken);
            stageWatch.Stop();
            readElapsed = stageWatch.Elapsed;
            applyReadBuffer(readBuffer);
            log($"Script stage: read completed: {FormatBytes(readBuffer.Length)} in {FormatDuration(readElapsed)} ({FormatSpeed(readBuffer.Length, readElapsed)})");
            log("Script stage: verify started");
            stageWatch.Restart();
            readOk = await programmer.VerifyAsync(chip, startAddress, readBuffer, progress, cancellationToken);
            stageWatch.Stop();
            verifyElapsed = stageWatch.Elapsed;
        }

        log(readOk
            ? $"Script stage: verify completed OK: {FormatBytes(length)} in {FormatDuration(verifyElapsed)} ({FormatSpeed(length, verifyElapsed)})"
            : $"Script stage: verify failed: {FormatBytes(length)} in {FormatDuration(verifyElapsed)} ({FormatSpeed(length, verifyElapsed)})");
        log(readOk ? "Script completed: read + verify OK" : "Script completed: read + verify failed");
    }

    private static async Task RunEraseWriteVerifyScriptAsync(
        IChipProgrammer programmer,
        ChipProfile chip,
        byte[] buffer,
        int startAddress,
        bool skipBlankPages,
        bool unprotectFirst,
        IProgress<int> progress,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        log($"Script request: erase, write and verify {FormatBytes(buffer.Length)} at 0x{startAddress:X6}");
        await UnprotectIfRequestedAsync(programmer, chip, unprotectFirst, progress, log, cancellationToken);
        log("Script stage: erase started");
        TimeSpan eraseElapsed;
        TimeSpan writeElapsed;
        TimeSpan finalVerifyElapsed;
        bool ok;
        if (programmer is RT809HSDKProgrammer rt809hWriter)
        {
            var result = await rt809hWriter.EraseWriteVerifyAsync(
                chip,
                startAddress,
                buffer,
                skipBlankPages,
                progress,
                progress,
                progress,
                elapsed =>
                {
                    eraseElapsed = elapsed;
                    log($"Script stage: erase completed in {FormatDuration(eraseElapsed)}");
                },
                () => log("Script stage: write started"),
                elapsed =>
                {
                    writeElapsed = elapsed;
                    log($"Script stage: write completed: {FormatBytes(buffer.Length)} in {FormatDuration(writeElapsed)} ({FormatSpeed(buffer.Length, writeElapsed)})");
                },
                () => log("Script stage: verify started"),
                cancellationToken);
            eraseElapsed = result.EraseElapsed;
            writeElapsed = result.WriteElapsed;
            finalVerifyElapsed = result.VerifyElapsed;
            ok = result.Verified;
        }
        else
        {
            var eraseWriteVerifyWatch = Stopwatch.StartNew();
            await programmer.EraseAsync(chip, progress, cancellationToken);
            eraseWriteVerifyWatch.Stop();
            eraseElapsed = eraseWriteVerifyWatch.Elapsed;
            log($"Script stage: erase completed in {FormatDuration(eraseElapsed)}");
            await UnprotectIfRequestedAsync(programmer, chip, unprotectFirst, progress, log, cancellationToken);
            log("Script stage: write started");
            eraseWriteVerifyWatch.Restart();
            await programmer.WriteAsync(chip, startAddress, buffer, progress, skipBlankPages, cancellationToken);
            eraseWriteVerifyWatch.Stop();
            writeElapsed = eraseWriteVerifyWatch.Elapsed;
            log($"Script stage: write completed: {FormatBytes(buffer.Length)} in {FormatDuration(writeElapsed)} ({FormatSpeed(buffer.Length, writeElapsed)})");
            log("Script stage: verify started");
            eraseWriteVerifyWatch.Restart();
            ok = await programmer.VerifyAsync(chip, startAddress, buffer, progress, cancellationToken);
            eraseWriteVerifyWatch.Stop();
            finalVerifyElapsed = eraseWriteVerifyWatch.Elapsed;
        }

        log(ok
            ? $"Script stage: verify completed OK: {FormatBytes(buffer.Length)} in {FormatDuration(finalVerifyElapsed)} ({FormatSpeed(buffer.Length, finalVerifyElapsed)})"
            : $"Script stage: verify failed: {FormatBytes(buffer.Length)} in {FormatDuration(finalVerifyElapsed)} ({FormatSpeed(buffer.Length, finalVerifyElapsed)})");
        log(ok ? "Script completed: verify OK" : "Script completed: verify failed");
    }
}
