using System.IO;
using NexusProgrammer;
using Xunit;

namespace NexusProgrammer.Tests;

public class BiosBufferServiceTests
{
    [Fact]
    public void TrimMetadataRemovesXgproMarkerAndFollowingBytes()
    {
        byte[] marker =
        [
            0x2D, 0x43, 0x6F, 0x6E, 0x66, 0x69, 0x67, 0x75,
            0x72, 0x61, 0x74, 0x69, 0x6F, 0x6E, 0x2D, 0x00
        ];
        byte[] input = [0xAA, 0xBB, .. marker, 0xCC, 0xDD];

        var result = BiosBufferService.TrimMetadata(input);

        Assert.Equal([0xAA, 0xBB], result.Buffer);
        Assert.Equal("XGecu metadata marker at 0x000002", result.Reason);
        Assert.Equal(marker.Length + 2, result.RemovedBytes);
    }

    [Fact]
    public void TrimMetadataRemovesSmallTrailingBytesAfterValidBiosSize()
    {
        var input = Enumerable.Repeat((byte)0xFF, 512 * 1024 + 1024).ToArray();

        var result = BiosBufferService.TrimMetadata(input);

        Assert.Equal(512 * 1024, result.Buffer.Length);
        Assert.Equal("valid BIOS size 512 KB with 1 KB trailing bytes", result.Reason);
        Assert.Equal(1024, result.RemovedBytes);
    }

    [Fact]
    public void TrimMetadataLeavesUnknownSizeUnchanged()
    {
        byte[] input = [0x01, 0x02, 0x03];

        var result = BiosBufferService.TrimMetadata(input);

        Assert.Same(input, result.Buffer);
        Assert.Equal(string.Empty, result.Reason);
        Assert.Equal(0, result.RemovedBytes);
    }

    [Fact]
    public void MergeMemoryBuffersConcatenatesInOrder()
    {
        var memories = new[]
        {
            new MemoryBufferOption("BIOS 1", [0x01, 0x02], string.Empty),
            new MemoryBufferOption("BIOS 2", [0xAA], string.Empty),
            new MemoryBufferOption("BIOS 3", [0xFE, 0xFF], string.Empty)
        };

        var merged = BiosBufferService.MergeMemoryBuffers(memories);

        Assert.Equal([0x01, 0x02, 0xAA, 0xFE, 0xFF], merged);
    }

    [Fact]
    public void UniqueFileNameSkipsExistingFilesAndUsedNames()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"nexus-buffer-name-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllBytes(Path.Combine(directory, "BIOS.bin"), []);
            var usedNames = new[] { "BIOS_2.bin" };

            var name = BiosBufferService.UniqueFileName(directory, "BIOS.bin", usedNames);

            Assert.Equal("BIOS_3.bin", name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
