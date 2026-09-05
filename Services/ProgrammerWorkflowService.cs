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
}
