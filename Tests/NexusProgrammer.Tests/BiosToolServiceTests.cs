using NexusProgrammer;
using Xunit;

namespace NexusProgrammer.Tests;

public class BiosToolServiceTests
{
    [Theory]
    [InlineData(16 * 1024 * 1024, "16MB_MERGED.bin")]
    [InlineData(12 * 1024 * 1024, "12MB_MERGED.bin")]
    public void MergedBiosFileNameUsesBinaryMegabytes(int bytes, string expected)
    {
        Assert.Equal(expected, BiosToolService.MergedBiosFileNameFor(bytes));
    }

    [Fact]
    public void SplitedBiosFileNameKeepsExistingAppSpelling()
    {
        Assert.Equal("8MB_SPLITED.bin", BiosToolService.SplitedBiosFileNameFor(8 * 1024 * 1024));
    }

    [Fact]
    public void Unlock8Fc8FileNameFallsBackToBios()
    {
        Assert.Equal("BIOS_8FC8_UNLOCKED.bin", BiosToolService.Unlock8Fc8FileNameFor(string.Empty));
    }

    [Fact]
    public void OemUnlockFileNameUsesSourceStemAndKind()
    {
        Assert.Equal("dump_ASUS_UNLOCK.bin", BiosToolService.OemUnlockFileNameFor(@"C:\bios\dump.bin", OemPasswordUnlockKind.Asus));
    }

    [Fact]
    public void ClearMeFileNameUsesCurrentChipWhenSourceIsEmpty()
    {
        var memory = new MemoryBufferOption("Memory 1", [], string.Empty);

        Assert.Equal("ACE25C512_CLEARME.bin", BiosToolService.ClearMeFileNameFor(memory, "ACE25C512"));
    }

    [Fact]
    public void SplitBiosReturnsRequestedParts()
    {
        byte[] buffer = [0x01, 0x02, 0x03, 0x04, 0x05];

        var (first, second) = BiosToolService.SplitBios(buffer, firstLength: 2, secondLength: 3);

        Assert.Equal([0x01, 0x02], first);
        Assert.Equal([0x03, 0x04, 0x05], second);
    }

    [Fact]
    public void SplitBiosRejectsOutOfRangeLengths()
    {
        byte[] buffer = [0x01, 0x02, 0x03];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BiosToolService.SplitBios(buffer, firstLength: 2, secondLength: 2));
    }
}
