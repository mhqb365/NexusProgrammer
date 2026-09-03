namespace NexusProgrammer;

internal static class Unlock8Fc8Service
{
    private static readonly byte[] IntelSignature = [0x5A, 0xA5, 0xF0, 0x0F, 0x03];

    public static Unlock8Fc8Result Unlock(byte[] source)
    {
        if (source.Length == 0)
        {
            return new Unlock8Fc8Result(false, source, 0, "Buffer is empty.");
        }

        if (!ContainsAtOrBefore(source, IntelSignature, 0x1000))
        {
            return new Unlock8Fc8Result(false, source, 0, "Intel signature not found.");
        }

        var patched = source.ToArray();
        var patchCount = 0;
        patchCount += PatchPattern(patched, 0xFC);
        patchCount += PatchPattern(patched, 0xFD);

        return patchCount > 0
            ? new Unlock8Fc8Result(true, patched, patchCount, $"Patched {patchCount} 8FC8 lock pattern(s).")
            : new Unlock8Fc8Result(false, source, 0, "8FC8 lock pattern not found.");
    }

    private static bool ContainsAtOrBefore(byte[] source, byte[] pattern, int maxOffset)
    {
        var limit = Math.Min(maxOffset, source.Length - pattern.Length);
        for (var i = 0; i <= limit; i++)
        {
            if (source.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static int PatchPattern(byte[] data, byte opcode)
    {
        var count = 0;
        var limit = Math.Min(0x160000, data.Length - 7);
        for (var i = 0; i <= limit; i++)
        {
            if (data[i] != 0x00 || data[i + 1] != opcode || data[i + 2] != 0xAA)
            {
                continue;
            }

            var oneByteGap = data[i + 4] == 0x00 && data[i + 5] == 0x00 && data[i + 6] == 0x00;
            var twoByteGap = i + 7 < data.Length
                && data[i + 5] == 0x00
                && data[i + 6] == 0x00
                && data[i + 7] == 0x00;
            if (!oneByteGap && !twoByteGap)
            {
                continue;
            }

            data[i + 2] = 0x00;
            count++;
        }

        return count;
    }
}

internal sealed record Unlock8Fc8Result(bool Success, byte[] Bios, int PatchCount, string Message);
