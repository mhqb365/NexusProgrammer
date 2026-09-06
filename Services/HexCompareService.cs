namespace NexusProgrammer;

public sealed record HexCompareResult(int Length, IReadOnlyList<int> DifferenceOffsets)
{
    public int DifferenceCount => DifferenceOffsets.Count;
}

public static class HexCompareService
{
    public static HexCompareResult Compare(byte[] first, byte[] second)
    {
        var length = Math.Max(first.Length, second.Length);
        var offsets = new List<int>();
        for (var i = 0; i < length; i++)
        {
            var firstHasByte = i < first.Length;
            var secondHasByte = i < second.Length;
            if (firstHasByte != secondHasByte || firstHasByte && first[i] != second[i])
            {
                offsets.Add(i);
            }
        }

        return new HexCompareResult(length, offsets);
    }

    public static int FindNextDifference(HexCompareResult result, int currentOffset)
    {
        if (result.DifferenceOffsets.Count == 0)
        {
            return -1;
        }

        var index = UpperBound(result.DifferenceOffsets, currentOffset);
        return index < result.DifferenceOffsets.Count ? result.DifferenceOffsets[index] : result.DifferenceOffsets[0];
    }

    public static int FindPreviousDifference(HexCompareResult result, int currentOffset)
    {
        if (result.DifferenceOffsets.Count == 0)
        {
            return -1;
        }

        var index = LowerBound(result.DifferenceOffsets, currentOffset) - 1;
        return index >= 0 ? result.DifferenceOffsets[index] : result.DifferenceOffsets[^1];
    }

    public static int FindFirstEqual(HexCompareResult result) =>
        FindFirstEqualInRange(result, 0, result.Length - 1);

    public static int FindLastEqual(HexCompareResult result) =>
        FindLastEqualInRange(result, 0, result.Length - 1);

    public static int FindNextEqual(HexCompareResult result, int currentOffset)
    {
        var offset = FindFirstEqualInRange(result, currentOffset + 1, result.Length - 1);
        return offset >= 0 ? offset : FindFirstEqualInRange(result, 0, currentOffset);
    }

    public static int FindPreviousEqual(HexCompareResult result, int currentOffset)
    {
        var offset = FindLastEqualInRange(result, 0, currentOffset - 1);
        return offset >= 0 ? offset : FindLastEqualInRange(result, currentOffset, result.Length - 1);
    }

    private static int FindFirstEqualInRange(HexCompareResult result, int start, int end)
    {
        if (result.Length == 0 || start > end)
        {
            return -1;
        }

        var candidate = Math.Max(0, start);
        end = Math.Min(end, result.Length - 1);
        for (var i = LowerBound(result.DifferenceOffsets, candidate); i < result.DifferenceOffsets.Count; i++)
        {
            var diff = result.DifferenceOffsets[i];
            if (diff > end)
            {
                break;
            }

            if (diff > candidate)
            {
                return candidate;
            }

            candidate++;
            if (candidate > end)
            {
                return -1;
            }
        }

        return candidate <= end ? candidate : -1;
    }

    private static int FindLastEqualInRange(HexCompareResult result, int start, int end)
    {
        if (result.Length == 0 || start > end)
        {
            return -1;
        }

        start = Math.Max(0, start);
        var candidate = Math.Min(end, result.Length - 1);
        for (var i = UpperBound(result.DifferenceOffsets, candidate) - 1; i >= 0; i--)
        {
            var diff = result.DifferenceOffsets[i];
            if (diff < start)
            {
                break;
            }

            if (diff < candidate)
            {
                return candidate;
            }

            candidate--;
            if (candidate < start)
            {
                return -1;
            }
        }

        return candidate >= start ? candidate : -1;
    }

    private static int LowerBound(IReadOnlyList<int> offsets, int value)
    {
        var left = 0;
        var right = offsets.Count;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (offsets[middle] < value)
            {
                left = middle + 1;
            }
            else
            {
                right = middle;
            }
        }

        return left;
    }

    private static int UpperBound(IReadOnlyList<int> offsets, int value)
    {
        var left = 0;
        var right = offsets.Count;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (offsets[middle] <= value)
            {
                left = middle + 1;
            }
            else
            {
                right = middle;
            }
        }

        return left;
    }
}
