namespace NexusProgrammer;

public interface IChipProgrammer
{
    string Name { get; }
    Task<bool> DetectAsync(IProgress<int> progress, CancellationToken cancellationToken = default);
    Task<byte[]> ReadIdAsync(ChipProfile chip, IProgress<int> progress, CancellationToken cancellationToken = default);
    Task<byte[]> ReadAsync(ChipProfile chip, int startAddress, int length, IProgress<int> progress, CancellationToken cancellationToken = default);
    Task WriteAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, bool skipBlankPages = false, CancellationToken cancellationToken = default);
    Task<bool> VerifyAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, CancellationToken cancellationToken = default);
    Task UnprotectAsync(ChipProfile chip, IProgress<int> progress, CancellationToken cancellationToken = default);
    Task EraseAsync(ChipProfile chip, IProgress<int> progress, CancellationToken cancellationToken = default);
}


