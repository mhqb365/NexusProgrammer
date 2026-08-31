using System.IO;
using Rt809hDevice = RT809H.SDK.RT809HProgrammer;

namespace NexusProgrammer;

public sealed class RT809HSDKProgrammer : IChipProgrammer
{
    public string Name => "RT809H SDK";

    public static bool CanOpenDevice() => Rt809hDevice.IsConnected();

    public Task<bool> DetectAsync(IProgress<int> progress) => Task.Run(() =>
    {
        progress.Report(10);
        using var device = Rt809hDevice.Open();
        progress.Report(100);
        return true;
    });

    public Task<byte[]> ReadIdAsync(ChipProfile chip, IProgress<int> progress) => Task.Run(() =>
    {
        EnsureSpi25(chip);
        using var device = Rt809hDevice.Open(Uses1V8Profile(chip));
        progress.Report(50);
        var id = device.ReadId();
        if (!id.IsValid || id.Manufacturer == 0xFF && id.MemoryType == 0xFF && id.CapacityCode == 0xFF)
        {
            throw new IOException($"Poor IC contact / Invalid IC ID {id}");
        }

        progress.Report(100);
        return new[] { id.Manufacturer, id.MemoryType, id.CapacityCode };
    });

    public async Task<byte[]> ReadAsync(ChipProfile chip, int startAddress, int length, IProgress<int> progress)
    {
        EnsureSupported(chip, startAddress, length);
        await using var device = Rt809hDevice.Open(Uses1V8Profile(chip));
        return await device.ReadAsync((uint)startAddress, length, progress);
    }

    public async Task<RT809HReadVerifyResult> ReadAndVerifyAsync(
        ChipProfile chip,
        int startAddress,
        int length,
        IProgress<int> readProgress,
        IProgress<int> verifyProgress,
        Action<byte[], TimeSpan>? readCompleted = null,
        Action? verifyStarted = null)
    {
        EnsureSupported(chip, startAddress, length);
        await using var device = Rt809hDevice.Open(Uses1V8Profile(chip));

        var readWatch = System.Diagnostics.Stopwatch.StartNew();
        var data = await device.ReadAsync((uint)startAddress, length, readProgress);
        readWatch.Stop();
        readCompleted?.Invoke(data, readWatch.Elapsed);

        verifyStarted?.Invoke();
        var verifyWatch = System.Diagnostics.Stopwatch.StartNew();
        var verified = await VerifyOpenDeviceAsync(device, (uint)startAddress, data, verifyProgress);
        verifyWatch.Stop();

        return new RT809HReadVerifyResult(data, verified, readWatch.Elapsed, verifyWatch.Elapsed);
    }

    public async Task WriteAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, bool skipBlankPages = false)
    {
        EnsureSupported(chip, startAddress, data.Length);
        await using var device = Rt809hDevice.Open(Uses1V8Profile(chip));
        await device.ProgramAsync((uint)startAddress, data, skipBlankPages, progress);
    }

    public async Task<RT809HEraseWriteVerifyResult> EraseWriteVerifyAsync(
        ChipProfile chip,
        int startAddress,
        byte[] data,
        bool skipBlankPages,
        IProgress<int> eraseProgress,
        IProgress<int> writeProgress,
        IProgress<int> verifyProgress,
        Action<TimeSpan>? eraseCompleted = null,
        Action? writeStarted = null,
        Action<TimeSpan>? writeCompleted = null,
        Action? verifyStarted = null)
    {
        EnsureSupported(chip, startAddress, data.Length);
        await using var device = Rt809hDevice.Open(Uses1V8Profile(chip));

        var eraseWatch = System.Diagnostics.Stopwatch.StartNew();
        await device.EraseAsync(EstimateEraseTimeout(chip), eraseProgress);
        eraseWatch.Stop();
        eraseCompleted?.Invoke(eraseWatch.Elapsed);

        writeStarted?.Invoke();
        var writeWatch = System.Diagnostics.Stopwatch.StartNew();
        await device.ProgramAsync((uint)startAddress, data, skipBlankPages, writeProgress);
        writeWatch.Stop();
        writeCompleted?.Invoke(writeWatch.Elapsed);

        verifyStarted?.Invoke();
        var verifyWatch = System.Diagnostics.Stopwatch.StartNew();
        var verified = await VerifyOpenDeviceAsync(device, (uint)startAddress, data, verifyProgress);
        verifyWatch.Stop();

        return new RT809HEraseWriteVerifyResult(verified, eraseWatch.Elapsed, writeWatch.Elapsed, verifyWatch.Elapsed);
    }

    public async Task<bool> VerifyAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress)
    {
        EnsureSupported(chip, startAddress, data.Length);
        await using var device = Rt809hDevice.Open(Uses1V8Profile(chip));
        try
        {
            await device.VerifyAsync((uint)startAddress, data, progress);
            return true;
        }
        catch (RT809H.SDK.RT809HException ex) when (ex.Status == 6)
        {
            return false;
        }
    }

    private static async Task<bool> VerifyOpenDeviceAsync(Rt809hDevice device, uint startAddress, byte[] data, IProgress<int> progress)
    {
        try
        {
            await device.VerifyAsync(startAddress, data, progress);
            return true;
        }
        catch (RT809H.SDK.RT809HException ex) when (ex.Status == 6)
        {
            return false;
        }
    }

    public Task UnprotectAsync(ChipProfile chip, IProgress<int> progress)
    {
        EnsureSpi25(chip);
        progress.Report(100);
        return Task.CompletedTask;
    }

    public async Task EraseAsync(ChipProfile chip, IProgress<int> progress)
    {
        EnsureSupported(chip, 0, chip.SizeBytes);
        await using var device = Rt809hDevice.Open(Uses1V8Profile(chip));
        await device.EraseAsync(EstimateEraseTimeout(chip), progress);
    }

    private static void EnsureSupported(ChipProfile chip, int startAddress, int length)
    {
        EnsureSpi25(chip);

        if (startAddress < 0 || length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startAddress), "RT809H SDK address and length must be non-negative.");
        }
    }

    private static void EnsureSpi25(ChipProfile chip)
    {
        if (!string.Equals(chip.Protocol, "SPI", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(chip.CommandSet, "25xx", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("RT809H SDK backend currently supports SPI 25xx flash only.");
        }
    }

    private static TimeSpan EstimateEraseTimeout(ChipProfile chip)
    {
        var mib = Math.Max(1.0, chip.SizeBytes / 1024.0 / 1024.0);
        return TimeSpan.FromSeconds(Math.Clamp(mib * 8.0, 30.0, 180.0));
    }

    private static bool Uses1V8Profile(ChipProfile chip) =>
        chip.Volts.Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .Contains("1.8", StringComparison.OrdinalIgnoreCase) ||
        chip.Volts.Contains("1V8", StringComparison.OrdinalIgnoreCase);
}

public sealed record RT809HReadVerifyResult(byte[] Data, bool Verified, TimeSpan ReadElapsed, TimeSpan VerifyElapsed);

public sealed record RT809HEraseWriteVerifyResult(bool Verified, TimeSpan EraseElapsed, TimeSpan WriteElapsed, TimeSpan VerifyElapsed);
