using System.IO;
using System.Runtime.InteropServices;

namespace NexusProgrammer;

public sealed class Ch347NativeProgrammer : IChipProgrammer
{
    private const int DeviceIndex = 0;
    private const uint ChipSelect = 0x80;
    private const int ReadChunkSize = 256 * 1024;
    private const int I2cReadChunkSize = 512;
    private const int WriteReadyTimeoutMs = 500;
    private const int SpiInitializationDelayMs = 50;
    private const int ReadIdMaxAttempts = 3;
    private const int ReadIdRetryDelayMs = 10;

    public string Name => "CH347 native DLL";

    public static bool IsAvailable =>
        File.Exists(Path.Combine(Environment.SystemDirectory, "CH347DLLA64.DLL")) ||
        File.Exists(Path.Combine(AppContext.BaseDirectory, "CH347DLLA64.DLL"));

    public static bool CanOpenDevice()
    {
        if (!WchUsbDeviceDetector.HasPresentDevice("VID_1A86", "PID_55DA", "PID_55DB"))
        {
            return false;
        }

        var handle = NativeMethods.CH347OpenDevice(DeviceIndex);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return false;
        }

        try
        {
            return NativeMethods.CH347SPI_Init(DeviceIndex, in SpiConfig.Default);
        }
        catch
        {
            return false;
        }
        finally
        {
            NativeMethods.CH347CloseDevice(DeviceIndex);
        }
    }

    public async Task<bool> DetectAsync(IProgress<int> progress)
    {
        progress.Report(10);
        await Task.Yield();
        var handle = NativeMethods.CH347OpenDevice(DeviceIndex);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            progress.Report(100);
            return false;
        }

        try
        {
            var ok = NativeMethods.CH347SPI_Init(DeviceIndex, in SpiConfig.Default);
            progress.Report(100);
            return ok;
        }
        finally
        {
            NativeMethods.CH347CloseDevice(DeviceIndex);
        }
    }

    public Task<byte[]> ReadIdAsync(ChipProfile chip, IProgress<int> progress) => Task.Run(() =>
    {
        if (IsI2c(chip))
        {
            progress.Report(100);
            return Array.Empty<byte>();
        }

        EnsureSpi(chip);
        progress.Report(25);
        byte[] id = [];
        for (var attempt = 1; attempt <= ReadIdMaxAttempts; attempt++)
        {
            // Some CH347 driver/firmware combinations return an idle bus on the
            // first session after connecting. Reopen and reinitialize SPI for
            // each retry so the retry follows the same path as a second click.
            using (OpenDevice())
            {
                id = SpiRead([0x9F], 3);
            }

            if (!IsInvalidJedecId(id))
            {
                break;
            }

            progress.Report(25 + attempt * 20);
            if (attempt < ReadIdMaxAttempts)
            {
                Thread.Sleep(ReadIdRetryDelayMs);
            }
        }

        progress.Report(100);
        return id;
    });

    public Task<byte[]> ReadAsync(ChipProfile chip, int startAddress, int length, IProgress<int> progress) => Task.Run(() =>
    {
        if (IsI2c(chip))
        {
            using var device = OpenDevice();
            return ReadI2cEeprom(chip, startAddress, length, progress);
        }

        EnsureSpi(chip);
        using var spiDevice = OpenDevice();
        var result = new byte[length];
        var done = 0;

        while (done < length)
        {
            var count = Math.Min(ReadChunkSize, length - done);
            var address = startAddress + done;
            var addressBytes = Uses4ByteAddress(chip, address) ? 4 : 3;
            var command = new byte[1 + addressBytes + 1];
            WriteAddress(command, 0, 0x0B, 0x0C, address, addressBytes);
            command[^1] = 0x00;
            var response = SpiRead(command, count);
            Buffer.BlockCopy(response, 0, result, done, count);
            done += count;
            progress.Report(length == 0 ? 100 : done * 100 / length);
        }

        progress.Report(100);
        return result;
    });

    public Task WriteAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, bool skipBlankPages = false) => Task.Run(async () =>
    {
        if (IsI2c(chip))
        {
            using var device = OpenDevice();
            await WriteI2cEepromAsync(chip, startAddress, data, progress, skipBlankPages);
            return;
        }

        EnsureSpi(chip);
        using var spiDevice = OpenDevice();
        var done = 0;
        var lastProgress = -1;

        while (done < data.Length)
        {
            var pageOffset = (startAddress + done) % chip.PageSize;
            var count = Math.Min(chip.PageSize - pageOffset, data.Length - done);
            if (skipBlankPages && IsBlank(data, done, count))
            {
                done += count;
                ReportProgress(progress, data.Length, done, ref lastProgress);
                continue;
            }

            WriteEnable();

            var address = startAddress + done;
            var addressBytes = Uses4ByteAddress(chip, address) ? 4 : 3;
            var headerLength = 1 + addressBytes;
            var command = new byte[count + headerLength];
            WriteAddress(command, 0, 0x02, 0x12, address, addressBytes);
            Buffer.BlockCopy(data, done, command, headerLength, count);
            SpiTransfer(command);
            await WaitUntilReadyAsync();

            done += count;
            ReportProgress(progress, data.Length, done, ref lastProgress);
        }

        progress.Report(100);
    });

    public async Task<bool> VerifyAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress)
    {
        var actual = await ReadAsync(chip, startAddress, data.Length, progress);
        return actual.SequenceEqual(data);
    }

    public Task UnprotectAsync(ChipProfile chip, IProgress<int> progress) => Task.Run(async () =>
    {
        if (IsI2c(chip))
        {
            throw new NotSupportedException("I2C EEPROM does not use SPI NOR block-protect status bits.");
        }

        EnsureSpi(chip);
        using var spiDevice = OpenDevice();
        await ClearSpiNorProtectionAsync(progress);
    });

    public Task EraseAsync(ChipProfile chip, IProgress<int> progress) => Task.Run(async () =>
    {
        if (IsI2c(chip))
        {
            using var device = OpenDevice();
            var blank = Enumerable.Repeat((byte)0xFF, chip.SizeBytes).ToArray();
            await WriteI2cEepromAsync(chip, 0, blank, progress, skipBlankPages: false);
            return;
        }

        EnsureSpi(chip);
        using var spiDevice = OpenDevice();
        WriteEnable();
        SpiTransfer([0xC7]);
        progress.Report(5);

        for (var i = 0; i < 600; i++)
        {
            if (!IsBusy())
            {
                progress.Report(100);
                return;
            }

            progress.Report(Math.Min(95, 5 + i / 7));
            await Task.Delay(100);
        }

        throw new TimeoutException("Erase timeout. Chip still reports WIP=1.");
    });

    private static Ch347Device OpenDevice()
    {
        var handle = NativeMethods.CH347OpenDevice(DeviceIndex);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            throw new InvalidOperationException("Cannot open CH347. Check USB connection, WCH CH347 driver, and that no other programmer software is using it.");
        }

        if (!NativeMethods.CH347SPI_Init(DeviceIndex, SpiConfig.Default))
        {
            NativeMethods.CH347CloseDevice(DeviceIndex);
            throw new InvalidOperationException("Cannot configure CH347 SPI controller.");
        }

        // The first transfer can return idle bus data if it starts immediately
        // after the controller changes the SPI pins and chip-select state.
        Thread.Sleep(SpiInitializationDelayMs);

        return new Ch347Device();
    }

    private static bool IsInvalidJedecId(byte[] id) =>
        id.Length == 0 || id.All(value => value == 0xFF) || id.All(value => value == 0x00);

    private static byte[] SpiTransfer(byte[] buffer)
    {
        var io = buffer.ToArray();
        if (!NativeMethods.CH347StreamSPI4(DeviceIndex, ChipSelect, (uint)io.Length, io))
        {
            if (!NativeMethods.CH347StreamSPI4(DeviceIndex, 0, (uint)io.Length, io))
            {
                throw new IOException("CH347 SPI transfer failed.");
            }
        }

        return io;
    }

    private static byte[] SpiRead(byte[] command, int readLength)
    {
        var io = new byte[command.Length + readLength];
        Buffer.BlockCopy(command, 0, io, 0, command.Length);
        var length = (uint)readLength;
        if (!NativeMethods.CH347SPI_Read(DeviceIndex, ChipSelect, (uint)command.Length, ref length, io))
        {
            throw new IOException("CH347 SPI read failed.");
        }

        if (length < readLength)
        {
            throw new IOException($"CH347 SPI read returned {length} byte(s), expected {readLength} byte(s).");
        }

        var result = new byte[readLength];
        Buffer.BlockCopy(io, 0, result, 0, readLength);
        return result;
    }

    private static byte[] ReadI2cEeprom(ChipProfile chip, int startAddress, int length, IProgress<int> progress)
    {
        var result = new byte[length];
        var done = 0;
        while (done < length)
        {
            var count = Math.Min(I2cReadChunkSize, length - done);
            var address = startAddress + done;
            var write = BuildI2cAddressWriteBuffer(chip, address);
            var read = new byte[count];

            if (!NativeMethods.CH347StreamI2C(DeviceIndex, (uint)write.Length, write, (uint)read.Length, read))
            {
                throw new IOException($"CH347 I2C read failed at 0x{address:X6}.");
            }

            Buffer.BlockCopy(read, 0, result, done, count);
            done += count;
            progress.Report(length == 0 ? 100 : done * 100 / length);
        }

        progress.Report(100);
        return result;
    }

    private static async Task WriteI2cEepromAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, bool skipBlankPages)
    {
        var done = 0;
        var lastProgress = -1;
        while (done < data.Length)
        {
            var address = startAddress + done;
            var pageOffset = address % chip.PageSize;
            var count = Math.Min(chip.PageSize - pageOffset, data.Length - done);
            if (skipBlankPages && IsBlank(data, done, count))
            {
                done += count;
                ReportProgress(progress, data.Length, done, ref lastProgress);
                continue;
            }

            var write = BuildI2cPageWriteBuffer(chip, address, data, done, count);

            if (!NativeMethods.CH347StreamI2C(DeviceIndex, (uint)write.Length, write, 0, Array.Empty<byte>()))
            {
                throw new IOException($"CH347 I2C write failed at 0x{address:X6}.");
            }

            done += count;
            ReportProgress(progress, data.Length, done, ref lastProgress);
            await Task.Delay(6);
        }

        progress.Report(100);
    }

    private static byte[] BuildI2cAddressWriteBuffer(ChipProfile chip, int address)
    {
        var device = I2cDeviceWriteAddress(chip, address);
        if (UsesOneByteI2cAddress(chip))
        {
            return [(byte)device, (byte)(address & 0xFF)];
        }

        return [(byte)device, (byte)((address >> 8) & 0xFF), (byte)(address & 0xFF)];
    }

    private static byte[] BuildI2cPageWriteBuffer(ChipProfile chip, int address, byte[] data, int dataOffset, int count)
    {
        var prefix = BuildI2cAddressWriteBuffer(chip, address);
        var write = new byte[prefix.Length + count];
        Buffer.BlockCopy(prefix, 0, write, 0, prefix.Length);
        Buffer.BlockCopy(data, dataOffset, write, prefix.Length, count);
        return write;
    }

    private static int I2cDeviceWriteAddress(ChipProfile chip, int address)
    {
        var block = UsesOneByteI2cAddress(chip) ? (address >> 8) & 0x07 : 0;
        return 0xA0 | (block << 1);
    }

    private static bool UsesOneByteI2cAddress(ChipProfile chip) => chip.SizeBytes <= 2048;

    private static bool IsBlank(byte[] data, int offset, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (data[offset + i] != 0xFF)
            {
                return false;
            }
        }

        return true;
    }

    private static void ReportProgress(IProgress<int> progress, int total, int done, ref int lastProgress)
    {
        var value = total == 0 ? 100 : Math.Clamp(done * 100 / total, 0, 100);
        if (value == lastProgress)
        {
            return;
        }

        lastProgress = value;
        progress.Report(value);
    }

    private static void WriteEnable() => SpiTransfer([0x06]);

    private static bool IsBusy()
    {
        var status = SpiTransfer([0x05, 0x00]);
        return (status[1] & 0x01) != 0;
    }

    private static byte ReadStatus(byte command) => SpiTransfer([command, 0x00])[1];

    private static async Task ClearSpiNorProtectionAsync(IProgress<int> progress)
    {
        var sr1 = ReadStatus(0x05);
        var sr2 = ReadStatus(0x35);
        progress.Report(20);

        var nextSr1 = (byte)(sr1 & 0x03);
        var nextSr2 = (byte)(sr2 & 0x02);
        if (nextSr1 == sr1 && nextSr2 == sr2)
        {
            progress.Report(100);
            return;
        }

        WriteEnable();
        SpiTransfer([0x01, nextSr1, nextSr2]);
        await WaitUntilReadyAsync();
        progress.Report(100);
    }

    private static Task WaitUntilReadyAsync()
    {
        var timeoutAt = Environment.TickCount64 + WriteReadyTimeoutMs;
        while (Environment.TickCount64 < timeoutAt)
        {
            if (!IsBusy())
            {
                return Task.CompletedTask;
            }

            Thread.Sleep(0);
        }

        throw new TimeoutException("Write timeout. Chip still reports WIP=1.");
    }

    private static void WriteAddress(byte[] buffer, int offset, byte command3Byte, byte command4Byte, int address, int addressBytes)
    {
        buffer[offset] = addressBytes == 4 ? command4Byte : command3Byte;
        if (addressBytes == 4)
        {
            buffer[offset + 1] = (byte)((address >> 24) & 0xFF);
            buffer[offset + 2] = (byte)((address >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((address >> 8) & 0xFF);
            buffer[offset + 4] = (byte)(address & 0xFF);
            return;
        }

        buffer[offset + 1] = (byte)((address >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((address >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(address & 0xFF);
    }

    private static bool Uses4ByteAddress(ChipProfile chip, int address) =>
        chip.SizeBytes > 0x1000000 || address > 0xFFFFFF;

    private static bool IsI2c(ChipProfile chip) => string.Equals(chip.Protocol, "I2C", StringComparison.OrdinalIgnoreCase);

    private static void EnsureSpi(ChipProfile chip)
    {
        if (!string.Equals(chip.Protocol, "SPI", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("This protocol is catalog-only in the CH347 backend. Real read/write is enabled for SPI 25xx and I2C 24xx.");
        }
    }

    private sealed class Ch347Device : IDisposable
    {
        public void Dispose() => NativeMethods.CH347CloseDevice(DeviceIndex);
    }

    private static class NativeMethods
    {
        [DllImport("CH347DLLA64.DLL", EntryPoint = "CH347OpenDevice", CallingConvention = CallingConvention.Winapi)]
        public static extern IntPtr CH347OpenDevice(int index);

        [DllImport("CH347DLLA64.DLL", EntryPoint = "CH347CloseDevice", CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CH347CloseDevice(int index);

        [DllImport("CH347DLLA64.DLL", EntryPoint = "CH347StreamSPI4", CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CH347StreamSPI4(int index, uint chipSelect, uint length, byte[] buffer);

        [DllImport("CH347DLLA64.DLL", EntryPoint = "CH347SPI_Init", CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CH347SPI_Init(int index, in SpiConfig spiConfig);

        [DllImport("CH347DLLA64.DLL", EntryPoint = "CH347SPI_Read", CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CH347SPI_Read(int index, uint chipSelect, uint writeLength, ref uint readLength, byte[] buffer);

        [DllImport("CH347DLLA64.DLL", EntryPoint = "CH347StreamI2C", CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CH347StreamI2C(int index, uint writeLength, byte[] writeBuffer, uint readLength, byte[] readBuffer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SpiConfig
    {
        private readonly byte mode;
        private readonly byte clock;
        private readonly byte byteOrder;
        private readonly ushort writeReadInterval;
        private readonly byte outDefaultData;
        private readonly uint chipSelect;
        private readonly byte cs1Polarity;
        private readonly byte cs2Polarity;
        private readonly ushort isAutoDeactivateCs;
        private readonly ushort activeDelay;
        private readonly uint delayDeactivate;

        private SpiConfig(
            byte mode,
            byte clock,
            byte byteOrder,
            ushort writeReadInterval,
            byte outDefaultData,
            uint chipSelect,
            byte cs1Polarity,
            byte cs2Polarity,
            ushort isAutoDeactivateCs,
            ushort activeDelay,
            uint delayDeactivate)
        {
            this.mode = mode;
            this.clock = clock;
            this.byteOrder = byteOrder;
            this.writeReadInterval = writeReadInterval;
            this.outDefaultData = outDefaultData;
            this.chipSelect = chipSelect;
            this.cs1Polarity = cs1Polarity;
            this.cs2Polarity = cs2Polarity;
            this.isAutoDeactivateCs = isAutoDeactivateCs;
            this.activeDelay = activeDelay;
            this.delayDeactivate = delayDeactivate;
        }

        public static readonly SpiConfig Default = new(
            mode: 0,
            clock: 1,
            byteOrder: 1,
            writeReadInterval: 0,
            outDefaultData: 0xFF,
            chipSelect: ChipSelect,
            cs1Polarity: 0,
            cs2Polarity: 0,
            isAutoDeactivateCs: 1,
            activeDelay: 0,
            delayDeactivate: 0);
    }
}

public sealed class ChNativeProgrammer : IChipProgrammer
{
    private const int DeviceIndex = 0;
    private const string ChNativeDll = "CH" + "341DLLA64.DLL";
    private const uint StreamMode = 0x81;
    private const uint ChipSelect = 0x80;
    private const int ReadChunkSize = 3840;
    private const int I2cReadChunkSize = 256;
    private const int PageProgramDelayMs = 1;

    public string Name => "CH341 native DLL";

    public static bool IsAvailable =>
        File.Exists(Path.Combine(Environment.SystemDirectory, ChNativeDll)) ||
        File.Exists(Path.Combine(AppContext.BaseDirectory, ChNativeDll));

    public static bool CanOpenDevice()
    {
        if (!WchUsbDeviceDetector.HasPresentDevice("VID_1A86", "PID_5512"))
        {
            return false;
        }

        var handle = NativeMethods.CHOpenDevice(DeviceIndex);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return false;
        }

        try
        {
            return NativeMethods.CHSetStream(DeviceIndex, StreamMode);
        }
        catch
        {
            return false;
        }
        finally
        {
            NativeMethods.CHCloseDevice(DeviceIndex);
        }
    }

    public async Task<bool> DetectAsync(IProgress<int> progress)
    {
        progress.Report(10);
        await Task.Yield();
        var handle = NativeMethods.CHOpenDevice(DeviceIndex);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            progress.Report(100);
            return false;
        }

        try
        {
            var ok = NativeMethods.CHSetStream(DeviceIndex, StreamMode);
            progress.Report(100);
            return ok;
        }
        finally
        {
            NativeMethods.CHCloseDevice(DeviceIndex);
        }
    }

    public Task<byte[]> ReadIdAsync(ChipProfile chip, IProgress<int> progress) => Task.Run(() =>
    {
        if (IsI2c(chip))
        {
            progress.Report(100);
            return Array.Empty<byte>();
        }

        EnsureSpi(chip);
        using var device = OpenDevice();
        progress.Report(25);
        var id = SpiTransfer([0x9F, 0x00, 0x00, 0x00]);
        progress.Report(100);
        return id.Skip(1).Take(3).ToArray();
    });

    public Task<byte[]> ReadAsync(ChipProfile chip, int startAddress, int length, IProgress<int> progress) => Task.Run(() =>
    {
        if (IsI2c(chip))
        {
            using var i2cDevice = OpenDevice();
            return ReadI2cEeprom(chip, startAddress, length, progress);
        }

        EnsureSpi(chip);
        using var spiDevice = OpenDevice();
        var result = new byte[length];
        var done = 0;

        while (done < length)
        {
            var count = Math.Min(ReadChunkSize, length - done);
            var address = startAddress + done;
            var addressBytes = Uses4ByteAddress(chip, address) ? 4 : 3;
            var command = new byte[count + 1 + addressBytes];
            WriteAddress(command, 0, 0x03, 0x13, address, addressBytes);
            var response = SpiTransfer(command);
            Buffer.BlockCopy(response, command.Length - count, result, done, count);
            done += count;
            progress.Report(length == 0 ? 100 : done * 100 / length);
        }

        progress.Report(100);
        return result;
    });

    public Task WriteAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, bool skipBlankPages = false) => Task.Run(async () =>
    {
        if (IsI2c(chip))
        {
            using var i2cDevice = OpenDevice();
            await WriteI2cEepromAsync(chip, startAddress, data, progress, skipBlankPages);
            return;
        }

        EnsureSpi(chip);
        using var spiDevice = OpenDevice();
        var done = 0;

        while (done < data.Length)
        {
            var pageOffset = (startAddress + done) % chip.PageSize;
            var count = Math.Min(chip.PageSize - pageOffset, data.Length - done);
            if (skipBlankPages && IsBlank(data, done, count))
            {
                done += count;
                progress.Report(data.Length == 0 ? 100 : done * 100 / data.Length);
                continue;
            }

            WriteEnable();

            var address = startAddress + done;
            var addressBytes = Uses4ByteAddress(chip, address) ? 4 : 3;
            var headerLength = 1 + addressBytes;
            var command = new byte[count + headerLength];
            WriteAddress(command, 0, 0x02, 0x12, address, addressBytes);
            Buffer.BlockCopy(data, done, command, headerLength, count);
            SpiTransfer(command);
            await WaitUntilReadyAsync();

            done += count;
            progress.Report(data.Length == 0 ? 100 : done * 100 / data.Length);
        }

        progress.Report(100);
    });

    public async Task<bool> VerifyAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress)
    {
        var actual = await ReadAsync(chip, startAddress, data.Length, progress);
        return actual.SequenceEqual(data);
    }

    public Task UnprotectAsync(ChipProfile chip, IProgress<int> progress) => Task.Run(async () =>
    {
        if (IsI2c(chip))
        {
            throw new NotSupportedException("I2C EEPROM does not use SPI NOR block-protect status bits.");
        }

        EnsureSpi(chip);
        using var spiDevice = OpenDevice();
        await ClearSpiNorProtectionAsync(progress);
    });

    public Task EraseAsync(ChipProfile chip, IProgress<int> progress) => Task.Run(async () =>
    {
        if (IsI2c(chip))
        {
            using var i2cDevice = OpenDevice();
            var blank = Enumerable.Repeat((byte)0xFF, chip.SizeBytes).ToArray();
            await WriteI2cEepromAsync(chip, 0, blank, progress, skipBlankPages: false);
            return;
        }

        EnsureSpi(chip);
        using var spiDevice = OpenDevice();
        WriteEnable();
        SpiTransfer([0xC7]);
        progress.Report(5);

        for (var i = 0; i < 600; i++)
        {
            if (!IsBusy())
            {
                progress.Report(100);
                return;
            }

            progress.Report(Math.Min(95, 5 + i / 7));
            await Task.Delay(100);
        }

        throw new TimeoutException("Erase timeout. Chip still reports WIP=1.");
    });

    private static ChDevice OpenDevice()
    {
        var handle = NativeMethods.CHOpenDevice(DeviceIndex);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            throw new InvalidOperationException("Cannot open CH. Check USB connection, WCH driver, and that no other programmer software is using it.");
        }

        if (!NativeMethods.CHSetStream(DeviceIndex, StreamMode))
        {
            NativeMethods.CHCloseDevice(DeviceIndex);
            throw new InvalidOperationException("Cannot configure CH SPI stream mode.");
        }

        return new ChDevice();
    }

    private static byte[] SpiTransfer(byte[] buffer)
    {
        var io = buffer.ToArray();
        if (!NativeMethods.CHStreamSPI4(DeviceIndex, ChipSelect, (uint)io.Length, io))
        {
            if (!NativeMethods.CHStreamSPI4(DeviceIndex, 0, (uint)io.Length, io))
            {
                throw new IOException("CH SPI transfer failed.");
            }
        }

        return io;
    }

    private static byte[] ReadI2cEeprom(ChipProfile chip, int startAddress, int length, IProgress<int> progress)
    {
        var result = new byte[length];
        var done = 0;
        while (done < length)
        {
            var count = Math.Min(I2cReadChunkSize, length - done);
            var address = startAddress + done;
            var write = BuildI2cAddressWriteBuffer(chip, address);
            var read = new byte[count];

            if (!NativeMethods.CHStreamI2C(DeviceIndex, (uint)write.Length, write, (uint)read.Length, read))
            {
                throw new IOException($"CH I2C read failed at 0x{address:X6}.");
            }

            Buffer.BlockCopy(read, 0, result, done, count);
            done += count;
            progress.Report(length == 0 ? 100 : done * 100 / length);
        }

        progress.Report(100);
        return result;
    }

    private static async Task WriteI2cEepromAsync(ChipProfile chip, int startAddress, byte[] data, IProgress<int> progress, bool skipBlankPages)
    {
        var done = 0;
        while (done < data.Length)
        {
            var address = startAddress + done;
            var pageOffset = address % chip.PageSize;
            var count = Math.Min(chip.PageSize - pageOffset, data.Length - done);
            if (skipBlankPages && IsBlank(data, done, count))
            {
                done += count;
                progress.Report(data.Length == 0 ? 100 : done * 100 / data.Length);
                continue;
            }

            var write = BuildI2cPageWriteBuffer(chip, address, data, done, count);

            if (!NativeMethods.CHStreamI2C(DeviceIndex, (uint)write.Length, write, 0, Array.Empty<byte>()))
            {
                throw new IOException($"CH I2C write failed at 0x{address:X6}.");
            }

            done += count;
            progress.Report(data.Length == 0 ? 100 : done * 100 / data.Length);
            await Task.Delay(8);
        }

        progress.Report(100);
    }

    private static byte[] BuildI2cAddressWriteBuffer(ChipProfile chip, int address)
    {
        var device = I2cDeviceWriteAddress(chip, address);
        if (UsesOneByteI2cAddress(chip))
        {
            return [(byte)device, (byte)(address & 0xFF)];
        }

        return [(byte)device, (byte)((address >> 8) & 0xFF), (byte)(address & 0xFF)];
    }

    private static byte[] BuildI2cPageWriteBuffer(ChipProfile chip, int address, byte[] data, int dataOffset, int count)
    {
        var prefix = BuildI2cAddressWriteBuffer(chip, address);
        var write = new byte[prefix.Length + count];
        Buffer.BlockCopy(prefix, 0, write, 0, prefix.Length);
        Buffer.BlockCopy(data, dataOffset, write, prefix.Length, count);
        return write;
    }

    private static int I2cDeviceWriteAddress(ChipProfile chip, int address)
    {
        var block = UsesOneByteI2cAddress(chip) ? (address >> 8) & 0x07 : 0;
        return 0xA0 | (block << 1);
    }

    private static bool UsesOneByteI2cAddress(ChipProfile chip) => chip.SizeBytes <= 2048;

    private static bool IsBlank(byte[] data, int offset, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (data[offset + i] != 0xFF)
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteEnable() => SpiTransfer([0x06]);

    private static bool IsBusy()
    {
        var status = SpiTransfer([0x05, 0x00]);
        return (status[1] & 0x01) != 0;
    }

    private static byte ReadStatus(byte command) => SpiTransfer([command, 0x00])[1];

    private static async Task ClearSpiNorProtectionAsync(IProgress<int> progress)
    {
        var sr1 = ReadStatus(0x05);
        var sr2 = ReadStatus(0x35);
        progress.Report(20);

        var nextSr1 = (byte)(sr1 & 0x03);
        var nextSr2 = (byte)(sr2 & 0x02);
        if (nextSr1 == sr1 && nextSr2 == sr2)
        {
            progress.Report(100);
            return;
        }

        WriteEnable();
        SpiTransfer([0x01, nextSr1, nextSr2]);
        await WaitUntilReadyAsync();
        progress.Report(100);
    }

    private static async Task WaitUntilReadyAsync()
    {
        for (var i = 0; i < 500; i++)
        {
            if (!IsBusy())
            {
                return;
            }

            await Task.Delay(PageProgramDelayMs);
        }

        throw new TimeoutException("Write timeout. Chip still reports WIP=1.");
    }

    private static void WriteAddress(byte[] buffer, int offset, byte command3Byte, byte command4Byte, int address, int addressBytes)
    {
        buffer[offset] = addressBytes == 4 ? command4Byte : command3Byte;
        if (addressBytes == 4)
        {
            buffer[offset + 1] = (byte)((address >> 24) & 0xFF);
            buffer[offset + 2] = (byte)((address >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((address >> 8) & 0xFF);
            buffer[offset + 4] = (byte)(address & 0xFF);
            return;
        }

        buffer[offset + 1] = (byte)((address >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((address >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(address & 0xFF);
    }

    private static bool Uses4ByteAddress(ChipProfile chip, int address) =>
        chip.SizeBytes > 0x1000000 || address > 0xFFFFFF;

    private static bool IsI2c(ChipProfile chip) => string.Equals(chip.Protocol, "I2C", StringComparison.OrdinalIgnoreCase);

    private static void EnsureSpi(ChipProfile chip)
    {
        if (!string.Equals(chip.Protocol, "SPI", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("This protocol is catalog-only in the CH backend. Real read/write is enabled for SPI 25xx and I2C 24xx.");
        }
    }

    private sealed class ChDevice : IDisposable
    {
        public void Dispose() => NativeMethods.CHCloseDevice(DeviceIndex);
    }

    private static class NativeMethods
    {
        [DllImport(ChNativeDll, EntryPoint = "CH" + "341OpenDevice", CallingConvention = CallingConvention.Winapi)]
        public static extern IntPtr CHOpenDevice(int index);

        [DllImport(ChNativeDll, EntryPoint = "CH" + "341CloseDevice", CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CHCloseDevice(int index);

        [DllImport(ChNativeDll, EntryPoint = "CH" + "341GetVerIC", CallingConvention = CallingConvention.Winapi)]
        public static extern uint CHGetVerIC(int index);

        [DllImport(ChNativeDll, EntryPoint = "CH" + "341SetStream", CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CHSetStream(int index, uint mode);

        [DllImport(ChNativeDll, EntryPoint = "CH" + "341StreamSPI4", CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CHStreamSPI4(int index, uint chipSelect, uint length, byte[] buffer);

        [DllImport(ChNativeDll, EntryPoint = "CH" + "341StreamI2C", CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CHStreamI2C(int index, uint writeLength, byte[] writeBuffer, uint readLength, byte[] readBuffer);
    }
}

internal static class WchUsbDeviceDetector
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;
    private const uint SpdrpHardwareId = 0x00000001;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public static bool HasPresentDevice(string vid, params string[] pids)
    {
        var hardwareIds = EnumeratePresentHardwareIds();
        return hardwareIds.Any(id =>
            id.Contains(vid, StringComparison.OrdinalIgnoreCase) &&
            pids.Any(pid => id.Contains(pid, StringComparison.OrdinalIgnoreCase)));
    }

    private static IEnumerable<string> EnumeratePresentHardwareIds()
    {
        var deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
            IntPtr.Zero,
            null,
            IntPtr.Zero,
            DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == InvalidHandleValue)
        {
            yield break;
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new SpDevInfoData
                {
                    cbSize = Marshal.SizeOf<SpDevInfoData>()
                };
                if (!NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref data))
                {
                    yield break;
                }

                foreach (var hardwareId in GetHardwareIds(deviceInfoSet, data))
                {
                    yield return hardwareId;
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static IEnumerable<string> GetHardwareIds(IntPtr deviceInfoSet, SpDevInfoData data)
    {
        var buffer = new byte[1024];
        if (!NativeMethods.SetupDiGetDeviceRegistryProperty(
            deviceInfoSet,
            ref data,
            SpdrpHardwareId,
            out _,
            buffer,
            (uint)buffer.Length,
            out var requiredSize))
        {
            yield break;
        }

        var length = Math.Min(buffer.Length, checked((int)requiredSize));
        var text = System.Text.Encoding.Unicode.GetString(buffer, 0, length).TrimEnd('\0');
        foreach (var value in text.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return value;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public int cbSize;
        public Guid classGuid;
        public uint devInst;
        public IntPtr reserved;
    }

    private static class NativeMethods
    {
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(
            IntPtr classGuid,
            string? enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInfo(
            IntPtr deviceInfoSet,
            uint memberIndex,
            ref SpDevInfoData deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr deviceInfoSet,
            ref SpDevInfoData deviceInfoData,
            uint property,
            out uint propertyRegDataType,
            byte[] propertyBuffer,
            uint propertyBufferSize,
            out uint requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
    }
}


