using System.IO;
using NexusProgrammer;
using Xunit;

namespace NexusProgrammer.Tests;

public class LargeFileIoTests
{
    [Fact]
    public async Task WriteAndReadAllBytesAsyncRoundTripsBinaryData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexus-large-io-{Guid.NewGuid():N}.bin");
        var data = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();

        try
        {
            await LargeFileIo.WriteAllBytesAsync(path, data);

            var read = await LargeFileIo.ReadAllBytesAsync(path);

            Assert.Equal(data, read);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ReadAllBytesAsyncHonorsAlreadyCanceledToken()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            LargeFileIo.ReadAllBytesAsync("unused.bin", cancellation.Token));
    }
}
