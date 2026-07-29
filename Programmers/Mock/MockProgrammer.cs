namespace NexusProgrammer;

public sealed class MockProgrammer : IChipProgrammer
{
    public string Name => "mock CH341";

    public async Task<bool> DetectAsync(IProgress<int> progress)
    {
        await SimulateAsync(progress, 250);
        return false;
    }

    public async Task<byte[]> ReadIdAsync(ChipProfile chip, IProgress<int> progress)
    {
        await SimulateAsync(progress, 300);
        return chip.CommandSet switch
        {
            "25xx" => [0xEF, 0x40, ChipDensityCode(chip.SizeBytes)],
            "24xx" => [0x50, 0x00, 0x00],
            _ => [0x93, 0x00, 0x00]
        };
    }

    public async Task<byte[]> ReadAsync(ChipProfile chip, int startAddress, int length, IProgress<int> progress)
    {
        var data = new byte[length];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((startAddress + i) & 0xFF);
            if (i % 4096 == 0)
            {
                progress.Report(data.Length == 0 ? 100 : i * 100 / data.Length);
                await Task.Delay(1);
            }
        }

        progress.Report(100);
        return data;
    }

    public Task WriteAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, bool skipBlankPages = false) =>
        SimulateBlocksAsync(data.Length, progress);

    public async Task<bool> VerifyAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress)
    {
        await SimulateBlocksAsync(data.Length, progress);
        return true;
    }

    public Task UnprotectAsync(ChipProfile chip, IProgress<int> progress) => SimulateAsync(progress, 200);

    public Task EraseAsync(ChipProfile chip, IProgress<int> progress) => SimulateAsync(progress, 700);

    private static async Task SimulateBlocksAsync(int length, IProgress<int> progress)
    {
        var blocks = Math.Max(1, length / 4096);
        for (var i = 0; i <= blocks; i++)
        {
            progress.Report(i * 100 / blocks);
            await Task.Delay(4);
        }
    }

    private static async Task SimulateAsync(IProgress<int> progress, int durationMs)
    {
        for (var i = 0; i <= 10; i++)
        {
            progress.Report(i * 10);
            await Task.Delay(durationMs / 10);
        }
    }

    private static byte ChipDensityCode(int sizeBytes) => sizeBytes switch
    {
        <= 1024 * 1024 => 0x14,
        <= 2 * 1024 * 1024 => 0x15,
        <= 4 * 1024 * 1024 => 0x16,
        <= 8 * 1024 * 1024 => 0x17,
        _ => 0x18
    };
}

