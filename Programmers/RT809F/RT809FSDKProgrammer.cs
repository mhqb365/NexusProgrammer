using System.IO;
using Rt809fDevice = RT809F.SDK.RT809FProgrammer;

namespace NexusProgrammer;

public sealed class RT809FSDKProgrammer : IChipProgrammer
{
    private const int MaximumAddressSpace = 0x1000000;

    public string Name => "RT809F SDK";

    public static bool CanOpenDevice() => Rt809fDevice.IsConnected();

    public Task<bool> DetectAsync(IProgress<int> progress) => Task.Run(() =>
    {
        progress.Report(10);
        using var device = Rt809fDevice.Open();
        progress.Report(100);
        return true;
    });

    public Task<byte[]> ReadIdAsync(ChipProfile chip, IProgress<int> progress) => Task.Run(() =>
    {
        EnsureSupported(chip, 0, 0);
        using var device = Rt809fDevice.Open();
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
        await using var device = Rt809fDevice.Open();
        return await device.ReadAsync((uint)startAddress, length, progress);
    }

    public async Task WriteAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, bool skipBlankPages = false)
    {
        EnsureSupported(chip, startAddress, data.Length);
        await using var device = Rt809fDevice.Open();
        await device.ProgramAsync((uint)startAddress, data, skipBlankPages, progress);
    }

    public async Task<bool> VerifyAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress)
    {
        var actual = await ReadAsync(chip, startAddress, data.Length, progress);
        return actual.SequenceEqual(data);
    }

    public Task UnprotectAsync(ChipProfile chip, IProgress<int> progress)
    {
        EnsureSupported(chip, 0, 0);
        progress.Report(100);
        return Task.CompletedTask;
    }

    public async Task EraseAsync(ChipProfile chip, IProgress<int> progress)
    {
        EnsureSupported(chip, 0, chip.SizeBytes);
        await using var device = Rt809fDevice.Open();
        await device.EraseAsync(EstimateEraseTimeout(chip), progress);
    }

    private static void EnsureSupported(ChipProfile chip, int startAddress, int length)
    {
        if (!string.Equals(chip.Protocol, "SPI", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(chip.CommandSet, "25xx", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("RT809F SDK backend currently supports SPI 25xx flash only.");
        }

        if (startAddress < 0 || length < 0 ||
            (long)startAddress + length > MaximumAddressSpace ||
            chip.SizeBytes > MaximumAddressSpace)
        {
            throw new NotSupportedException("RT809F SDK currently supports 24-bit addressing up to 16 MiB only.");
        }
    }

    private static TimeSpan EstimateEraseTimeout(ChipProfile chip)
    {
        var mib = Math.Max(1.0, chip.SizeBytes / 1024.0 / 1024.0);
        return TimeSpan.FromSeconds(Math.Clamp(mib * 8.0, 30.0, 180.0));
    }
}
