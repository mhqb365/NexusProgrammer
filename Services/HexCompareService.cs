namespace NexusProgrammer;

public sealed record HexCompareResult(int Length, IReadOnlyList<int> DifferenceOffsets)
{
    public int DifferenceCount => DifferenceOffsets.Count;
    public IReadOnlyList<int> EqualOffsets => Enumerable.Range(0, Length).Except(DifferenceOffsets).ToList();
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

        return result.DifferenceOffsets.FirstOrDefault(offset => offset > currentOffset, result.DifferenceOffsets[0]);
    }

    public static int FindPreviousDifference(HexCompareResult result, int currentOffset)
    {
        if (result.DifferenceOffsets.Count == 0)
        {
            return -1;
        }

        for (var i = result.DifferenceOffsets.Count - 1; i >= 0; i--)
        {
            if (result.DifferenceOffsets[i] < currentOffset)
            {
                return result.DifferenceOffsets[i];
            }
        }

        return result.DifferenceOffsets[^1];
    }

    public static int FindFirstEqual(HexCompareResult result) =>
        result.EqualOffsets.Count == 0 ? -1 : result.EqualOffsets[0];

    public static int FindLastEqual(HexCompareResult result) =>
        result.EqualOffsets.Count == 0 ? -1 : result.EqualOffsets[^1];

    public static int FindNextEqual(HexCompareResult result, int currentOffset) =>
        FindNext(result.EqualOffsets, currentOffset);

    public static int FindPreviousEqual(HexCompareResult result, int currentOffset) =>
        FindPrevious(result.EqualOffsets, currentOffset);

    private static int FindNext(IReadOnlyList<int> offsets, int currentOffset)
    {
        if (offsets.Count == 0)
        {
            return -1;
        }

        return offsets.FirstOrDefault(offset => offset > currentOffset, offsets[0]);
    }

    private static int FindPrevious(IReadOnlyList<int> offsets, int currentOffset)
    {
        if (offsets.Count == 0)
        {
            return -1;
        }

        for (var i = offsets.Count - 1; i >= 0; i--)
        {
            if (offsets[i] < currentOffset)
            {
                return offsets[i];
            }
        }

        return offsets[^1];
    }
}
