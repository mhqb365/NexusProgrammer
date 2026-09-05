using NexusProgrammer;
using Xunit;

namespace NexusProgrammer.Tests;

public sealed class ProgrammerCancellationTests
{
    private static readonly ChipProfile TestChip = new(
        Name: "TEST",
        Protocol: "SPI_NOR",
        SizeBytes: 1024,
        PageSize: 256,
        CommandSet: "25xx",
        Manufacturer: "TEST",
        Volts: "3.3",
        Type: "SPI_NOR");

    [Fact]
    public async Task MockReadHonorsAlreadyCanceledToken()
    {
        var programmer = new MockProgrammer();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            programmer.ReadAsync(TestChip, 0, 16, new Progress<int>(), cts.Token));
    }

    [Fact]
    public async Task MockEraseHonorsAlreadyCanceledToken()
    {
        var programmer = new MockProgrammer();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            programmer.EraseAsync(TestChip, new Progress<int>(), cts.Token));
    }
}
