using System.IO;
using Rt809fDevice = RT809F.SDK.RT809FProgrammer;

namespace NexusProgrammer;

public sealed class RT809FSDKProgrammer : IChipProgrammer
{
    public string Name => "RT809F SDK";

    public static bool CanOpenDevice() => Rt809fDevice.IsConnected();

    public Task<bool> DetectAsync(IProgress<int> progress, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(10);
        using var device = Rt809fDevice.Open();
        progress.Report(100);
        return true;
    }, cancellationToken);

    public Task<byte[]> ReadIdAsync(ChipProfile chip, IProgress<int> progress, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSpi25(chip);
        using var device = Rt809fDevice.Open();
        progress.Report(50);
        var id = device.ReadId();
        if (!id.IsValid || id.Manufacturer == 0xFF && id.MemoryType == 0xFF && id.CapacityCode == 0xFF)
        {
            throw new IOException($"Poor IC contact / Invalid IC ID {id}");
        }

        progress.Report(100);
        return new[] { id.Manufacturer, id.MemoryType, id.CapacityCode };
    }, cancellationToken);

    public async Task<byte[]> ReadAsync(ChipProfile chip, int startAddress, int length, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        EnsureSupported(chip, startAddress, length);
        await using var device = Rt809fDevice.Open();
        return await device.ReadAsync((uint)startAddress, length, progress, cancellationToken);
    }

    public async Task WriteAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, bool skipBlankPages = false, CancellationToken cancellationToken = default)
    {
        EnsureSupported(chip, startAddress, data.Length);
        await using var device = Rt809fDevice.Open();
        await device.ProgramAsync((uint)startAddress, data, skipBlankPages, progress, cancellationToken);
    }

    public async Task<bool> VerifyAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        EnsureSupported(chip, startAddress, data.Length);
        await using var device = Rt809fDevice.Open();
        try
        {
            // Compare each incoming block inside the SDK. This avoids allocating
            // and copying a second full-ROM buffer merely to verify it.
            await device.VerifyAsync((uint)startAddress, data, progress, cancellationToken);
            return true;
        }
        catch (RT809F.SDK.RT809FException ex) when (ex.Status == 6)
        {
            return false;
        }
    }

    public Task UnprotectAsync(ChipProfile chip, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSpi25(chip);
        progress.Report(100);
        return Task.CompletedTask;
    }

    public async Task EraseAsync(ChipProfile chip, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        EnsureSupported(chip, 0, chip.SizeBytes);
        await using var device = Rt809fDevice.Open();
        await device.EraseAsync(EstimateEraseTimeout(chip), progress, cancellationToken);
    }

    private static void EnsureSupported(ChipProfile chip, int startAddress, int length)
    {
        EnsureSpi25(chip);

        if (startAddress < 0 || length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startAddress), "RT809F SDK address and length must be non-negative.");
        }
    }

    private static void EnsureSpi25(ChipProfile chip)
    {
        if (!string.Equals(chip.Protocol, "SPI", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(chip.CommandSet, "25xx", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("RT809F SDK backend currently supports SPI 25xx flash only.");
        }
    }

    private static TimeSpan EstimateEraseTimeout(ChipProfile chip)
    {
        var mib = Math.Max(1.0, chip.SizeBytes / 1024.0 / 1024.0);
        return TimeSpan.FromSeconds(Math.Clamp(mib * 8.0, 30.0, 180.0));
    }
}
