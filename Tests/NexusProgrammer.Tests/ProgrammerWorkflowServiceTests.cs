using NexusProgrammer;
using Xunit;

namespace NexusProgrammer.Tests;

public class ProgrammerWorkflowServiceTests
{
    [Fact]
    public void ResolveSelectionAutoPrefersCh341BeforeCh347()
    {
        var detection = new ProgrammerDetection(
            T48Detected: true,
            Rt809fDetected: true,
            Rt809hDetected: true,
            Ch347Detected: true,
            Ch341Detected: true);

        var selection = ProgrammerWorkflowService.ResolveSelection("auto", detection);

        Assert.Equal("ch341", selection.Key);
        Assert.Equal("CH341 connected", selection.StatusText);
        Assert.True(selection.IsConnected);
    }

    [Fact]
    public void ResolveSelectionUsesRequestedProgrammerWhenDetected()
    {
        var detection = new ProgrammerDetection(
            T48Detected: false,
            Rt809fDetected: false,
            Rt809hDetected: true,
            Ch347Detected: false,
            Ch341Detected: false);

        var selection = ProgrammerWorkflowService.ResolveSelection("rt809h", detection);

        Assert.Equal("rt809h", selection.Key);
        Assert.Equal("RT809H connected", selection.StatusText);
        Assert.True(selection.IsConnected);
    }

    [Fact]
    public void ResolveSelectionReportsSelectedProgrammerDisconnected()
    {
        var detection = new ProgrammerDetection(
            T48Detected: false,
            Rt809fDetected: false,
            Rt809hDetected: false,
            Ch347Detected: false,
            Ch341Detected: false);

        var selection = ProgrammerWorkflowService.ResolveSelection("t48", detection);

        Assert.Equal("none", selection.Key);
        Assert.Equal("XGecu T48 disconnected", selection.StatusText);
        Assert.False(selection.IsConnected);
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0x40, 0x18 }, "EF 40 18")]
    [InlineData(new byte[] { 0x00 }, "00")]
    public void FormatIdUsesUppercaseHexPairs(byte[] id, string expected)
    {
        Assert.Equal(expected, ProgrammerWorkflowService.FormatId(id));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF })]
    [InlineData(new byte[] { 0x03, 0x00, 0x00 })]
    public void IsInvalidJedecIdRejectsKnownBadIds(byte[] id)
    {
        Assert.True(ProgrammerWorkflowService.IsInvalidJedecId(id));
    }

    [Fact]
    public void IsInvalidJedecIdAcceptsNonBlankId()
    {
        Assert.False(ProgrammerWorkflowService.IsInvalidJedecId([0xEF, 0x40, 0x18]));
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(2 * 1024 * 1024, "2 MB")]
    public void FormatBytesMatchesUiText(int bytes, string expected)
    {
        Assert.Equal(expected, ProgrammerWorkflowService.FormatBytes(bytes));
    }

    [Fact]
    public void FormatSpeedUsesBytesAndElapsedTime()
    {
        Assert.Equal("2 MB/s", ProgrammerWorkflowService.FormatSpeed(4 * 1024 * 1024, TimeSpan.FromSeconds(2)));
    }

    [Theory]
    [InlineData("1.8V", true)]
    [InlineData("1 V8", true)]
    [InlineData("3.3V", false)]
    public void Requires1V8AdapterMatchesSupportedVoltageText(string volts, bool expected)
    {
        var chip = new ChipProfile("W25Q", "SPI_NOR", 1024, 256, "STD", Volts: volts);

        Assert.Equal(expected, ProgrammerWorkflowService.Requires1V8Adapter(chip));
    }

    [Theory]
    [InlineData("1.8 V", "1.8V", true)]
    [InlineData("3.3V", "1.8V", false)]
    public void SameVoltageProfileIgnoresSpacingAndTrailingV(string leftVolts, string rightVolts, bool expected)
    {
        var left = new ChipProfile("A", "SPI_NOR", 1024, 256, "STD", Volts: leftVolts);
        var right = new ChipProfile("B", "SPI_NOR", 1024, 256, "STD", Volts: rightVolts);

        Assert.Equal(expected, ProgrammerWorkflowService.SameVoltageProfile(left, right));
    }

    [Fact]
    public void ChipMatchesIdRequiresSameProfileNameAndJedecId()
    {
        var chip = new ChipProfile("W25Q128", "SPI_NOR", 16 * 1024 * 1024, 256, "STD");
        var catalog = new[]
        {
            Candidate("MX25L128", "EF 40 18"),
            Candidate("W25Q128", "EF 40 18")
        };

        Assert.True(ProgrammerWorkflowService.ChipMatchesId(chip, [0xEF, 0x40, 0x18], catalog));
        Assert.False(ProgrammerWorkflowService.ChipMatchesId(chip, [0xC2, 0x20, 0x18], catalog));
    }

    [Theory]
    [InlineData("100", 0x100)]
    [InlineData("0x100", 0x100)]
    [InlineData("  00AF  ", 0xAF)]
    [InlineData("not-hex", 0)]
    public void ParseStartAddressUsesHexAndFallsBackToZero(string text, int expected)
    {
        Assert.Equal(expected, ProgrammerWorkflowService.ParseStartAddress(text));
    }

    [Fact]
    public async Task ReadChipAsyncLogsRequestAndReturnsReadData()
    {
        var programmer = new RecordingProgrammer();
        programmer.ReadData = [0x12, 0x34];
        var chip = new ChipProfile("W25Q", "SPI_NOR", 2, 256, "STD");
        var log = new List<string>();

        var data = await ProgrammerWorkflowService.ReadChipAsync(programmer, chip, 0x20, 2, new Progress<int>(), log.Add, CancellationToken.None);

        Assert.Equal([0x12, 0x34], data);
        Assert.Equal(["Read request: 2 B from 0x000020"], log);
        Assert.Contains("Read:20:2", programmer.Calls);
    }

    [Fact]
    public async Task WriteChipAsyncCanUnprotectBeforeWrite()
    {
        var programmer = new RecordingProgrammer();
        var chip = new ChipProfile("W25Q", "SPI_NOR", 2, 256, "STD", Volts: "3.3V");
        var log = new List<string>();

        await ProgrammerWorkflowService.WriteChipAsync(programmer, chip, 0x30, [0xAA, 0xBB], skipBlankPages: true, unprotectFirst: true, new Progress<int>(), log.Add, CancellationToken.None);

        Assert.Equal(["Unprotect", "Write:30:2:True"], programmer.Calls);
        Assert.Equal(
            [
                "Write request: 2 B to 0x000030 (skip FF pages), voltage profile 3.3V",
                "Unprotect request: W25Q",
                "Unprotect completed"
            ],
            log);
    }

    [Fact]
    public async Task VerifyChipAsyncLogsResult()
    {
        var programmer = new RecordingProgrammer { VerifyResult = false };
        var chip = new ChipProfile("W25Q", "SPI_NOR", 2, 256, "STD");
        var log = new List<string>();

        var ok = await ProgrammerWorkflowService.VerifyChipAsync(programmer, chip, 0x40, [0xAA, 0xBB], new Progress<int>(), log.Add, CancellationToken.None);

        Assert.False(ok);
        Assert.Equal(["Verify request: 2 B at 0x000040", "Verify failed"], log);
    }

    [Fact]
    public async Task EraseChipAsyncCanUnprotectBeforeErase()
    {
        var programmer = new RecordingProgrammer();
        var chip = new ChipProfile("W25Q", "SPI_NOR", 2, 256, "STD");
        var log = new List<string>();

        await ProgrammerWorkflowService.EraseChipAsync(programmer, chip, unprotectFirst: true, new Progress<int>(), log.Add, CancellationToken.None);

        Assert.Equal(["Unprotect", "Erase"], programmer.Calls);
        Assert.Equal(["Unprotect request: W25Q", "Unprotect completed"], log);
    }

    private static IcCandidate Candidate(string name, string jedecId)
    {
        var profile = new ChipProfile(name, "SPI_NOR", 1024, 256, "STD");
        return new IcCandidate(name, "3.3V", "8 Mbits", "256 Bytes", "GENERIC", "SPI_NOR", profile, jedecId);
    }

    private sealed class RecordingProgrammer : IChipProgrammer
    {
        public string Name => "Recording";
        public List<string> Calls { get; } = [];
        public byte[] ReadData { get; set; } = [];
        public bool VerifyResult { get; set; } = true;

        public Task<bool> DetectAsync(IProgress<int> progress, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<byte[]> ReadIdAsync(ChipProfile chip, IProgress<int> progress, CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<byte>());

        public Task<byte[]> ReadAsync(ChipProfile chip, int startAddress, int length, IProgress<int> progress, CancellationToken cancellationToken = default)
        {
            Calls.Add($"Read:{startAddress:X}:{length}");
            return Task.FromResult(ReadData);
        }

        public Task WriteAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, bool skipBlankPages = false, CancellationToken cancellationToken = default)
        {
            Calls.Add($"Write:{startAddress:X}:{data.Length}:{skipBlankPages}");
            return Task.CompletedTask;
        }

        public Task<bool> VerifyAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, CancellationToken cancellationToken = default)
        {
            Calls.Add($"Verify:{startAddress:X}:{data.Length}");
            return Task.FromResult(VerifyResult);
        }

        public Task UnprotectAsync(ChipProfile chip, IProgress<int> progress, CancellationToken cancellationToken = default)
        {
            Calls.Add("Unprotect");
            return Task.CompletedTask;
        }

        public Task EraseAsync(ChipProfile chip, IProgress<int> progress, CancellationToken cancellationToken = default)
        {
            Calls.Add("Erase");
            return Task.CompletedTask;
        }
    }
}
