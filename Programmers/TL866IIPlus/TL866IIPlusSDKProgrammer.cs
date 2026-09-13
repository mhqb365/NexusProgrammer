using TL866IIPlusSdk;

namespace NexusProgrammer;

public sealed class TL866IIPlusSDKProgrammer : IChipProgrammer
{
    public string Name => "TL866IIPlus SDK";

    public static bool CanOpenDevice()
    {
        try
        {
            using var device = TL866IIPlusUsbDevice.OpenFirst();
            return device.Info.IsTl866IIPlus;
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> DetectAsync(IProgress<int> progress, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(10);
        using var device = TL866IIPlusUsbDevice.OpenFirst();
        progress.Report(100);
        return device.Info.IsTl866IIPlus;
    }, cancellationToken);

    public Task<byte[]> ReadIdAsync(ChipProfile chip, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSpi25(chip);
        progress.Report(100);
        throw ProtocolNotEnabled();
    }

    public Task<byte[]> ReadAsync(ChipProfile chip, int startAddress, int length, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSpi25(chip);
        progress.Report(100);
        throw ProtocolNotEnabled();
    }

    public Task WriteAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, bool skipBlankPages = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSpi25(chip);
        progress.Report(100);
        throw ProtocolNotEnabled();
    }

    public Task<bool> VerifyAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSpi25(chip);
        progress.Report(100);
        throw ProtocolNotEnabled();
    }

    public Task UnprotectAsync(ChipProfile chip, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSpi25(chip);
        progress.Report(100);
        return Task.CompletedTask;
    }

    public Task EraseAsync(ChipProfile chip, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSpi25(chip);
        progress.Report(100);
        throw ProtocolNotEnabled();
    }

    private static void EnsureSpi25(ChipProfile chip)
    {
        if (!string.Equals(chip.Protocol, "SPI", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(chip.CommandSet, "25xx", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("TL866IIPlus SDK backend is currently scoped to SPI 25xx flash only.");
        }
    }

    private static NotSupportedException ProtocolNotEnabled() =>
        new("TL866IIPlus native SDK detected the programmer, but SPI protocol commands are not enabled yet. Capture independent TL866IIPlus USB transfers from real hardware before enabling read/write/erase safely.");
}
