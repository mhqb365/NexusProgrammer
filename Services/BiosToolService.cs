using System.IO;
using System.Text;

namespace NexusProgrammer;

public static class BiosToolService
{
    public static string MergedBiosFileNameFor(int bytes) =>
        $"{FormatBinaryMegabytes(bytes)}_MERGED.bin";

    public static string SplitedBiosFileNameFor(int bytes) =>
        $"{FormatBinaryMegabytes(bytes)}_SPLITED.bin";

    public static string Unlock8Fc8FileNameFor(string sourceFileName)
    {
        var source = string.IsNullOrWhiteSpace(sourceFileName)
            ? "BIOS"
            : Path.GetFileNameWithoutExtension(sourceFileName);
        return $"{SafeFileStem(source)}_8FC8_UNLOCKED.bin";
    }

    public static string OemUnlockFileNameFor(string sourceFileName, OemPasswordUnlockKind kind)
    {
        var source = string.IsNullOrWhiteSpace(sourceFileName)
            ? "BIOS"
            : Path.GetFileNameWithoutExtension(sourceFileName);
        return $"{SafeFileStem(source)}_{kind.ToString().ToUpperInvariant()}_UNLOCK.bin";
    }

    public static string ClearMeFileNameFor(MemoryBufferOption memory, string fallbackChipName)
    {
        var source = string.IsNullOrWhiteSpace(memory.SourceFileName)
            ? fallbackChipName
            : Path.GetFileNameWithoutExtension(memory.SourceFileName);
        return $"{SafeFileStem(source)}_CLEARME.bin";
    }

    public static (byte[] First, byte[] Second) SplitBios(byte[] buffer, int firstLength, int secondLength)
    {
        if (firstLength < 0 || secondLength < 0 || firstLength + secondLength > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(firstLength), "Split lengths exceed BIOS buffer length.");
        }

        return (
            buffer[..firstLength].ToArray(),
            buffer[firstLength..(firstLength + secondLength)].ToArray());
    }

    private static string FormatBinaryMegabytes(int bytes)
    {
        const int mib = 1024 * 1024;
        if (bytes % mib == 0)
        {
            return $"{bytes / mib}MB";
        }

        return $"{bytes / (double)mib:0.##}MB";
    }

    private static string SafeFileStem(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        var stem = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(stem) ? "BIOS" : stem;
    }
}
