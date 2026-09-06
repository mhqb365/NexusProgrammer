using NexusProgrammer;
using Xunit;

namespace NexusProgrammer.Tests;

public class HexCompareServiceTests
{
    [Fact]
    public void FindDifferencesIncludesDifferentBytesAndSizeTail()
    {
        var result = HexCompareService.Compare([0x00, 0x01, 0x02], [0x00, 0xFF, 0x02, 0x03]);

        Assert.Equal([1, 3], result.DifferenceOffsets);
        Assert.Equal(4, result.Length);
    }

    [Fact]
    public void FindNextDifferenceWrapsForward()
    {
        var result = new HexCompareResult(10, [2, 8]);

        Assert.Equal(8, HexCompareService.FindNextDifference(result, 3));
        Assert.Equal(2, HexCompareService.FindNextDifference(result, 8));
    }

    [Fact]
    public void FindPreviousDifferenceWrapsBackward()
    {
        var result = new HexCompareResult(10, [2, 8]);

        Assert.Equal(2, HexCompareService.FindPreviousDifference(result, 7));
        Assert.Equal(8, HexCompareService.FindPreviousDifference(result, 2));
    }

    [Fact]
    public void FindEqualOffsetsNavigatesOnlyMatchingBytes()
    {
        var result = HexCompareService.Compare([0x00, 0x01, 0x02], [0x00, 0xFF, 0x02]);

        Assert.Equal(2, HexCompareService.FindNextEqual(result, 0));
        Assert.Equal(0, HexCompareService.FindNextEqual(result, 2));
        Assert.Equal(0, HexCompareService.FindPreviousEqual(result, 2));
        Assert.Equal(2, HexCompareService.FindPreviousEqual(result, 0));
        Assert.Equal(0, HexCompareService.FindFirstEqual(result));
        Assert.Equal(2, HexCompareService.FindLastEqual(result));
    }
}
