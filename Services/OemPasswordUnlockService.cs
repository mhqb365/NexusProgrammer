namespace NexusProgrammer;

internal enum OemPasswordUnlockKind
{
    Acer,
    Asus,
    Hp
}

internal static class OemPasswordUnlockService
{
    private static readonly byte[] AsusAmitseMarker = [0x41, 0x4D, 0x49, 0x54, 0x53, 0x45, 0x53, 0x65, 0x74, 0x75, 0x70, 0x00];
    private static readonly byte[] AcerOldPasswordMarker = [0x5F, 0x50, 0x53, 0x57, 0x5F];
    private static readonly byte[] AcerNewPasswordMarker = [0x5F, 0x55, 0x55, 0xAA, 0xAA, 0x5F];
    private static readonly byte[] HpNvramActiveMarker = "NvramActiveRegn\0"u8.ToArray();
    private static readonly byte[][] HpUnlockRequiredMarkers =
    [
        "H_AuthVar\0"u8.ToArray(),
        "H_SmartCover\0"u8.ToArray()
    ];
    private static readonly byte[][] HpUnlockOptionalMarkers =
    [
        "H_ShrdCrInf\0"u8.ToArray(),
        "H_MeFwEcSts\0"u8.ToArray()
    ];
    private static readonly byte[][] HpEcUnlockRequiredMarkers =
    [
        "H_AuthVar\0"u8.ToArray(),
        "H_ShrdCrInf\0"u8.ToArray(),
        "H_MeFwEcSts\0"u8.ToArray()
    ];

    private const int AsusUnlockZeroLength = 80;
    private const int AcerOldPasswordOffset = 0x10;
    private const int AcerOldUnlockZeroLength = 0x20;
    private const int AcerNewUnlockZeroLength = 80;
    private const int HpUnlockScanSize = 0x1000;

    public static OemPasswordUnlockResult Unlock(byte[] source, OemPasswordUnlockKind kind)
    {
        return kind switch
        {
            OemPasswordUnlockKind.Acer => UnlockAcer(source),
            OemPasswordUnlockKind.Asus => UnlockAsus(source),
            OemPasswordUnlockKind.Hp => UnlockHp(source),
            _ => new OemPasswordUnlockResult(false, source, [], "Unsupported unlock type.")
        };
    }

    private static OemPasswordUnlockResult UnlockAsus(byte[] source)
    {
        var data = source.ToArray();
        var markerCount = CountMarkers(data, AsusAmitseMarker);
        var ranges = ZeroAfterMarkers(data, AsusAmitseMarker, AsusAmitseMarker.Length, AsusUnlockZeroLength);

        if (markerCount == 0)
        {
            return new OemPasswordUnlockResult(false, source, [], "ASUS password marker was not found.");
        }

        return ranges.Count > 0
            ? new OemPasswordUnlockResult(true, data, ranges, $"Cleared {ranges.Count} ASUS password region(s).")
            : new OemPasswordUnlockResult(false, source, [], "ASUS password marker found, but no password payload was found.");
    }

    private static OemPasswordUnlockResult UnlockAcer(byte[] source)
    {
        var data = source.ToArray();
        var markerCount = CountMarkers(data, AcerOldPasswordMarker) + CountMarkers(data, AcerNewPasswordMarker);
        var ranges = new List<OemPasswordUnlockRange>();
        ranges.AddRange(ZeroAfterMarkers(data, AcerOldPasswordMarker, AcerOldPasswordOffset, AcerOldUnlockZeroLength));
        ranges.AddRange(ZeroAfterMarkers(data, AcerNewPasswordMarker, AcerNewPasswordMarker.Length, AcerNewUnlockZeroLength));

        if (markerCount == 0)
        {
            return new OemPasswordUnlockResult(false, source, [], "ACER password marker was not found.");
        }

        return ranges.Count > 0
            ? new OemPasswordUnlockResult(true, data, ranges, $"Cleared {ranges.Count} ACER password region(s).")
            : new OemPasswordUnlockResult(false, source, [], "ACER password marker found, but no password payload was found.");
    }

    private static OemPasswordUnlockResult UnlockHp(byte[] source)
    {
        var data = source.ToArray();
        var ranges = new List<OemPasswordUnlockRange>();
        var markerCount = 0;
        var markerOffset = IndexOf(data, HpNvramActiveMarker, 0);
        while (markerOffset >= 0)
        {
            markerCount++;
            var end = Math.Min(markerOffset + HpUnlockScanSize, data.Length);
            var clearLength = HpUnlockRegionLength(data, markerOffset, end);
            if (clearLength == 0)
            {
                clearLength = HpEcUnlockRegionLength(data, markerOffset, end);
            }

            if (clearLength > 0)
            {
                Array.Fill<byte>(data, 0xFF, markerOffset, clearLength);
                ranges.Add(new OemPasswordUnlockRange(markerOffset, clearLength));
            }

            markerOffset = IndexOf(data, HpNvramActiveMarker, markerOffset + HpNvramActiveMarker.Length);
        }

        if (markerCount == 0)
        {
            return new OemPasswordUnlockResult(false, source, [], "HP password marker was not found.");
        }

        return ranges.Count > 0
            ? new OemPasswordUnlockResult(true, data, ranges, $"Cleared {ranges.Count} HP password region(s).")
            : new OemPasswordUnlockResult(false, source, [], "HP password marker found, but no supported password payload was found.");
    }

    private static int HpUnlockRegionLength(byte[] data, int offset, int end)
    {
        if (!ContainsAll(data, offset, end, HpUnlockRequiredMarkers))
        {
            return 0;
        }

        if (!ContainsAny(data, offset, end, HpUnlockOptionalMarkers))
        {
            return 0;
        }

        return LastUsedLength(data, offset, end);
    }

    private static int HpEcUnlockRegionLength(byte[] data, int offset, int end)
    {
        return ContainsAll(data, offset, end, HpEcUnlockRequiredMarkers)
            ? LastUsedLength(data, offset, end)
            : 0;
    }

    private static List<OemPasswordUnlockRange> ZeroAfterMarkers(byte[] data, byte[] marker, int startDelta, int zeroLength)
    {
        var ranges = new List<OemPasswordUnlockRange>();
        var markerOffset = IndexOf(data, marker, 0);
        while (markerOffset >= 0)
        {
            var start = markerOffset + startDelta;
            var end = Math.Min(start + zeroLength, data.Length);
            if (start < end && HasPasswordPayload(data, start, end - start))
            {
                Array.Clear(data, start, end - start);
                ranges.Add(new OemPasswordUnlockRange(start, end - start));
            }

            markerOffset = IndexOf(data, marker, markerOffset + marker.Length);
        }

        return ranges;
    }

    private static int CountMarkers(byte[] data, byte[] marker)
    {
        var count = 0;
        var markerOffset = IndexOf(data, marker, 0);
        while (markerOffset >= 0)
        {
            count++;
            markerOffset = IndexOf(data, marker, markerOffset + marker.Length);
        }

        return count;
    }

    private static bool HasPasswordPayload(byte[] data, int offset, int length)
    {
        for (var i = offset; i < offset + length; i++)
        {
            if (data[i] is not 0x00 and not 0xFF)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAll(byte[] data, int offset, int end, IReadOnlyList<byte[]> markers)
    {
        foreach (var marker in markers)
        {
            if (IndexOf(data, marker, offset, end) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsAny(byte[] data, int offset, int end, IReadOnlyList<byte[]> markers)
    {
        foreach (var marker in markers)
        {
            if (IndexOf(data, marker, offset, end) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int LastUsedLength(byte[] data, int offset, int end)
    {
        var lastUsed = -1;
        for (var i = offset; i < end; i++)
        {
            if (data[i] != 0xFF)
            {
                lastUsed = i - offset;
            }
        }

        return lastUsed >= HpNvramActiveMarker.Length ? lastUsed + 1 : 0;
    }

    private static int IndexOf(byte[] data, byte[] pattern, int start)
    {
        if (pattern.Length == 0 || data.Length < pattern.Length || start > data.Length - pattern.Length)
        {
            return -1;
        }

        for (var i = Math.Max(0, start); i <= data.Length - pattern.Length; i++)
        {
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOf(byte[] data, byte[] pattern, int start, int end)
    {
        if (pattern.Length == 0 || data.Length < pattern.Length || start > end - pattern.Length)
        {
            return -1;
        }

        for (var i = Math.Max(0, start); i <= end - pattern.Length; i++)
        {
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return i;
            }
        }

        return -1;
    }
}

internal sealed record OemPasswordUnlockResult(bool Success, byte[] Bios, IReadOnlyList<OemPasswordUnlockRange> ClearedRanges, string Message);

internal sealed record OemPasswordUnlockRange(int Offset, int Length);
