namespace NexusProgrammer;

public interface IChipProgrammer
{
    string Name { get; }
    Task<bool> DetectAsync(IProgress<int> progress);
    Task<byte[]> ReadIdAsync(ChipProfile chip, IProgress<int> progress);
    Task<byte[]> ReadAsync(ChipProfile chip, int startAddress, int length, IProgress<int> progress);
    Task WriteAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, bool skipBlankPages = false);
    Task<bool> VerifyAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress);
    Task UnprotectAsync(ChipProfile chip, IProgress<int> progress);
    Task EraseAsync(ChipProfile chip, IProgress<int> progress);
}


