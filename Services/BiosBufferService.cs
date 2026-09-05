using System.IO;

namespace NexusProgrammer;

public sealed record BiosTrimResult(byte[] Buffer, string Reason, int RemovedBytes);

public static class BiosBufferService
{
    private const int MaxTrailingMetadataBytes = 1024 * 1024;

    private static readonly int[] ValidBiosSizes =
    [
        512 * 1024,
        1 * 1024 * 1024,
        2 * 1024 * 1024,
        4 * 1024 * 1024,
        8 * 1024 * 1024,
        12 * 1024 * 1024,
        16 * 1024 * 1024,
        20 * 1024 * 1024,
        24 * 1024 * 1024,
        32 * 1024 * 1024,
        40 * 1024 * 1024,
        48 * 1024 * 1024,
        64 * 1024 * 1024,
        128 * 1024 * 1024
    ];

    private static readonly byte[] XgproMetadataMarker =
    [
        0x2D, 0x43, 0x6F, 0x6E, 0x66, 0x69, 0x67, 0x75,
        0x72, 0x61, 0x74, 0x69, 0x6F, 0x6E, 0x2D, 0x00
    ];

    public static BiosTrimResult TrimMetadata(byte[] buffer)
    {
        var trimmed = StripXgproMetadata(buffer, out var markerOffset, out var removedBytes);
        if (removedBytes > 0)
        {
            return new BiosTrimResult(trimmed, $"XGecu metadata marker at 0x{markerOffset:X6}", removedBytes);
        }

        var targetSize = ValidBiosSizes
            .Where(size => size < buffer.Length)
            .DefaultIfEmpty(0)
            .Max();
        if (targetSize == 0)
        {
            return new BiosTrimResult(buffer, string.Empty, 0);
        }

        var excess = buffer.Length - targetSize;
        if (excess <= 0 || excess > MaxTrailingMetadataBytes)
        {
            return new BiosTrimResult(buffer, string.Empty, 0);
        }

        var result = new byte[targetSize];
        Buffer.BlockCopy(buffer, 0, result, 0, targetSize);
        return new BiosTrimResult(
            result,
            $"valid BIOS size {FormatBytes(targetSize)} with {FormatBytes(excess)} trailing bytes",
            excess);
    }

    public static byte[] MergeMemoryBuffers(IEnumerable<MemoryBufferOption> memories)
    {
        var list = memories.ToList();
        var merged = new byte[list.Sum(memory => memory.Buffer.Length)];
        var offset = 0;
        foreach (var memory in list)
        {
            Buffer.BlockCopy(memory.Buffer, 0, merged, offset, memory.Buffer.Length);
            offset += memory.Buffer.Length;
        }

        return merged;
    }

    public static string UniqueFileName(string directory, string fileName, IEnumerable<string>? usedNames = null)
    {
        var used = (usedNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = fileName;
        var index = 2;
        while (used.Contains(candidate) || File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{stem}_{index}{extension}";
            index++;
        }

        return candidate;
    }

    private static byte[] StripXgproMetadata(byte[] buffer, out int markerOffset, out int removedBytes)
    {
        markerOffset = -1;
        removedBytes = 0;

        var lastPossibleOffset = buffer.Length - XgproMetadataMarker.Length;
        if (lastPossibleOffset < 0)
        {
            return buffer;
        }

        markerOffset = HexSearchService.FindBytes(buffer, XgproMetadataMarker, lastPossibleOffset, forward: false);
        if (markerOffset < 0)
        {
            return buffer;
        }

        removedBytes = buffer.Length - markerOffset;
        var trimmed = new byte[markerOffset];
        Buffer.BlockCopy(buffer, 0, trimmed, 0, markerOffset);
        return trimmed;
    }

    private static string FormatBytes(int bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024.0):0.##} MB";
        }

        return bytes >= 1024 ? $"{bytes / 1024.0:0.##} KB" : $"{bytes} B";
    }
}
