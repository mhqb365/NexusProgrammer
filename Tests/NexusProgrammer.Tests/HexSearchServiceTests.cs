using NexusProgrammer;
using Xunit;

namespace NexusProgrammer.Tests;

public class HexSearchServiceTests
{
    [Theory]
    [InlineData("01 0A ff", new byte[] { 0x01, 0x0A, 0xFF })]
    [InlineData("010AFF", new byte[] { 0x01, 0x0A, 0xFF })]
    [InlineData("0x01,0x0A-FF", new byte[] { 0x01, 0x0A, 0xFF })]
    public void TryParseHexPatternAcceptsCommonSeparators(string text, byte[] expected)
    {
        var ok = HexSearchService.TryParseHexPattern(text, out var pattern);

        Assert.True(ok);
        Assert.Equal(expected, pattern);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("GG")]
    [InlineData("01:02")]
    public void TryParseHexPatternRejectsInvalidInput(string text)
    {
        var ok = HexSearchService.TryParseHexPattern(text, out var pattern);

        Assert.False(ok);
        Assert.Empty(pattern);
    }

    [Fact]
    public void FormatHexPatternUsesUppercaseBytePairs()
    {
        Assert.Equal("00 0A FF", HexSearchService.FormatHexPattern([0x00, 0x0A, 0xFF]));
    }

    [Theory]
    [InlineData("10", 0x10)]
    [InlineData("0x20", 0x20)]
    [InlineData("0000AB", 0xAB)]
    public void TryParseOffsetUsesHex(string text, int expected)
    {
        var ok = HexSearchService.TryParseOffset(text, out var offset);

        Assert.True(ok);
        Assert.Equal(expected, offset);
    }

    [Fact]
    public void FindBytesSearchesForwardAndBackward()
    {
        byte[] buffer = [0xAA, 0x01, 0x02, 0xBB, 0x01, 0x02];
        byte[] pattern = [0x01, 0x02];

        Assert.Equal(1, HexSearchService.FindBytes(buffer, pattern, 0, forward: true));
        Assert.Equal(4, HexSearchService.FindBytes(buffer, pattern, buffer.Length - 1, forward: false));
    }

    [Fact]
    public void FindAllBytesReturnsOverlappingMatches()
    {
        byte[] buffer = [0xAA, 0xAA, 0xAA];
        byte[] pattern = [0xAA, 0xAA];

        Assert.Equal([0, 1], HexSearchService.FindAllBytes(buffer, pattern));
    }

    [Fact]
    public void FindAsciiTextIgnoresCase()
    {
        var buffer = System.Text.Encoding.ASCII.GetBytes("aaBxxAAb");
        var pattern = System.Text.Encoding.ASCII.GetBytes("AAB");

        Assert.Equal(0, HexSearchService.FindAsciiText(buffer, pattern, 0, forward: true));
        Assert.Equal(5, HexSearchService.FindAsciiText(buffer, pattern, buffer.Length - 1, forward: false));
        Assert.Equal([0, 5], HexSearchService.FindAllAsciiText(buffer, pattern));
    }
}
