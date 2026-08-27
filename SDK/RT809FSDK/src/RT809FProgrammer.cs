using System.Buffers;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RT809F.SDK;

public readonly record struct JedecId(byte Manufacturer, byte MemoryType, byte CapacityCode)
{
    public bool IsValid => Manufacturer is not (0x00 or 0xFF);
    public override string ToString() => $"{Manufacturer:X2} {MemoryType:X2} {CapacityCode:X2}";
}

public sealed class RT809FException(string message, int status, Exception? inner = null) : IOException(message, inner)
{
    public int Status { get; } = status;
}

/// <summary>Thread-safe .NET 8 SDK for the RT809F SPI-NOR interface.</summary>
public sealed class RT809FProgrammer : IDisposable, IAsyncDisposable
{
    private const uint FtdiId = 0x04036010;
    private const int ReadBlockSize = 256 * 1024;
    private const int MpsseReadLimit = 64 * 1024;
    private const int PageSize = 256;
    private const int ProgramBatchSize = 63_720;
    private const ulong AddressSpaceSize = 0x1_0000_0000UL;
    private const byte PinsIdle = 0x08, PinsActive = 0x00, PinDirections = 0x0B;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FtdiHandle? _handle;
    private string? _controlSerial;
    private delegate void BlockConsumer(ReadOnlySpan<byte> block, uint address);

    private RT809FProgrammer(FtdiHandle handle, string? controlSerial)
    {
        _handle = handle;
        _controlSerial = controlSerial;
    }

    public static bool IsConnected()
    {
        try
        {
            Native.EnsureLoaded();
            if (Native.FT_CreateDeviceInfoList(out var count) != 0) return false;
            for (uint i = 0; i < count; i++)
            {
                var serial = new byte[32];
                var description = new byte[128];
                if (Native.FT_GetDeviceInfoDetail(i, out _, out _, out var id, out _, serial, description, out _) == 0 && id == FtdiId)
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex) when (ex is RT809FException or DllNotFoundException or BadImageFormatException) { return false; }
    }

    public static RT809FProgrammer Open()
    {
        Native.EnsureLoaded();
        CheckNative(Native.FT_CreateDeviceInfoList(out var count), "FT_CreateDeviceInfoList");
        string? selected = null, controlSerial = null;
        for (uint i = 0; i < count; i++)
        {
            var serial = new byte[32]; var description = new byte[128];
            if (Native.FT_GetDeviceInfoDetail(i, out _, out _, out var id, out _, serial, description, out _) != 0 || id != FtdiId) continue;
            var deviceSerial = Ascii(serial); var deviceDescription = Ascii(description);
            if (deviceDescription.EndsWith(" B", StringComparison.OrdinalIgnoreCase)) controlSerial = deviceSerial;
            selected ??= deviceSerial;
            if (deviceDescription.EndsWith(" A", StringComparison.OrdinalIgnoreCase)) selected = deviceSerial;
        }
        if (string.IsNullOrEmpty(selected)) throw new RT809FException("RT809F not found.", 2);
        if (!string.IsNullOrEmpty(controlSerial)) PrepareControlChannelBeforeSpi(controlSerial);
        CheckNative(Native.FT_OpenEx(System.Text.Encoding.ASCII.GetBytes(selected + '\0'), 1, out var raw), "FT_OpenEx");
        var handle = new FtdiHandle(raw);
        try
        {
            Configure(handle);
            if (!string.IsNullOrEmpty(controlSerial)) PrepareControlChannelAfterSpi(controlSerial);
            var programmer = new RT809FProgrammer(handle, controlSerial);
            programmer.WakeFlash();
            return programmer;
        }
        catch { handle.Dispose(); throw; }
    }

    public JedecId ReadId()
    {
        using var lease = Enter();
        // The vendor software issues WREN before JEDEC ID on this programmer.
        Command(0x06);
        Span<byte> reply = stackalloc byte[3]; Transaction([0x9F], reply);
        Command(0x04);
        return new(reply[0], reply[1], reply[2]);
    }

    public async Task<byte[]> ReadAsync(uint address, int length, IProgress<int>? progress = null, CancellationToken token = default)
    {
        ValidateRange(address, length);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var result = GC.AllocateUninitializedArray<byte>(length);
                ReadCore(address, result, progress, token); return result;
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task BlankCheckAsync(uint address, int length, IProgress<int>? progress = null, CancellationToken token = default)
    {
        ValidateRange(address, length);
        await RunLockedAsync(() => ProcessReadBlocks(address, length, progress, token, (block, current) =>
        {
            var index = block.IndexOfAnyExcept((byte)0xFF);
            if (index >= 0) throw new RT809FException($"Flash is not blank at 0x{current + (uint)index:X8}.", 6);
        }), token).ConfigureAwait(false);
    }

    public async Task EraseAsync(TimeSpan timeout, IProgress<int>? progress = null, CancellationToken token = default)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        await RunLockedAsync(() =>
        {
            Command(0x06); Command(0xC7);
            var started = DateTime.UtcNow;
            while ((ReadStatus() & 1) != 0)
            {
                token.ThrowIfCancellationRequested();
                var elapsed = DateTime.UtcNow - started;
                if (elapsed >= timeout) throw new TimeoutException("RT809F chip erase timed out.");
                progress?.Report((int)Math.Clamp(elapsed.TotalMilliseconds / timeout.TotalMilliseconds * 100, 0, 99));
                Thread.Sleep(25);
            }
            progress?.Report(100);
        }, token).ConfigureAwait(false);
    }

    public async Task ProgramAsync(uint address, ReadOnlyMemory<byte> data, bool skipBlankPages = false,
        IProgress<int>? progress = null, CancellationToken token = default)
    {
        ValidateRange(address, data.Length); var owned = data.ToArray();
        await RunLockedAsync(() =>
        {
            var done = 0;
            var batch = new List<byte>(ProgramBatchSize);
            while (done < owned.Length)
            {
                token.ThrowIfCancellationRequested();
                var current = address + (uint)done;
                var count = Math.Min(PageSize - (int)(current % PageSize), owned.Length - done);
                var page = owned.AsSpan(done, count);
                if (!skipBlankPages || page.IndexOfAnyExcept((byte)0xFF) >= 0)
                {
                    var frameSize = count + ProgramFrameOverhead(current);
                    if (batch.Count > 0 && batch.Count + frameSize > ProgramBatchSize)
                    {
                        FlushProgramBatch(batch, token);
                        progress?.Report(ProgressPercent(done, owned.Length));
                    }

                    AppendProgramFrame(batch, current, page);
                }
                done += count;
            }

            if (batch.Count > 0)
            {
                FlushProgramBatch(batch, token);
            }
            progress?.Report(100);
        }, token).ConfigureAwait(false);
    }

    private void FlushProgramBatch(List<byte> batch, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        WriteAll(CollectionsMarshal.AsSpan(batch));
        batch.Clear();
        WaitReady(TimeSpan.FromSeconds(10), token);
    }

    private static int ProgramFrameOverhead(uint address) => Uses4ByteAddress(address) ? 40 : 39;

    private static void AppendProgramFrame(List<byte> batch, uint address, ReadOnlySpan<byte> page)
    {
        // Captured RT809F page frame: WREN, page program, then GPIO READY wait.
        var use4ByteAddress = Uses4ByteAddress(address);
        var commandLength = page.Length + (use4ByteAddress ? 5 : 4);
        batch.AddRange([0x80, PinsActive, PinDirections, 0x11, 0x00, 0x00, 0x06,
                        0x80, PinsIdle, PinDirections,
                        0x80, PinsActive, PinDirections, 0x11,
                        (byte)(commandLength - 1), (byte)((commandLength - 1) >> 8),
                        use4ByteAddress ? (byte)0x12 : (byte)0x02]);
        AppendAddress(batch, address, use4ByteAddress);
        foreach (var value in page) batch.Add(value);
        batch.AddRange([0x80, PinsIdle, PinDirections,
                        0x80, 0x0B, PinDirections,
                        0x82, 0xFE, 0x09,
                        0x82, 0xFF, 0x09,
                        0x82, 0xF7, 0x09,
                        0x89,
                        0x82, 0xFE, 0x09]);
    }

    public async Task VerifyAsync(uint address, ReadOnlyMemory<byte> expected, IProgress<int>? progress = null, CancellationToken token = default)
    {
        ValidateRange(address, expected.Length); var owned = expected.ToArray();
        await RunLockedAsync(() =>
        {
            var offset = 0;
            ProcessReadBlocks(address, owned.Length, progress, token, (block, current) =>
            {
                var expectedBlock = owned.AsSpan(offset, block.Length);
                if (!block.SequenceEqual(expectedBlock))
                {
                    var i = 0; while (block[i] == expectedBlock[i]) i++;
                    throw new RT809FException($"Verify failed at 0x{current + (uint)i:X8}.", 6);
                }
                offset += block.Length;
            });
        }, token).ConfigureAwait(false);
    }

    private async Task RunLockedAsync(Action operation, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { await Task.Run(operation, token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private void ProcessReadBlocks(uint address, int length, IProgress<int>? progress, CancellationToken token, BlockConsumer consume)
    {
        if (length == 0) { progress?.Report(100); return; }
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(ReadBlockSize, length));
        try
        {
            BeginContinuousRead(address, length);
            var done = 0;
            try
            {
                while (done < length)
                {
                    token.ThrowIfCancellationRequested(); var count = Math.Min(buffer.Length, length - done);
                    ClockRead(buffer.AsSpan(0, count)); consume(buffer.AsSpan(0, count), address + (uint)done);
                    done += count; progress?.Report(ProgressPercent(done, length));
                }
            }
            finally { EndContinuousRead(); }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private void ReadCore(uint address, Span<byte> output, IProgress<int>? progress, CancellationToken token)
    {
        if (output.IsEmpty) { progress?.Report(100); return; }
        BeginContinuousRead(address, output.Length);
        var done = 0;
        try
        {
            while (done < output.Length)
            {
                token.ThrowIfCancellationRequested(); var count = Math.Min(ReadBlockSize, output.Length - done);
                ClockRead(output.Slice(done, count)); done += count; progress?.Report(ProgressPercent(done, output.Length));
            }
        }
        finally { EndContinuousRead(); }
    }

    private void BeginContinuousRead(uint address, int length)
    {
        // Matches the vendor capture: WREN pulse, then one READ command while CS
        // remains asserted for every following 64 KiB clock block.
        var use4ByteAddress = Uses4ByteAddress(address, length);
        byte[] command =
        [
            0x80, PinsActive, PinDirections, 0x11, 0x00, 0x00, 0x06,
            0x80, PinsIdle, PinDirections,
            0x80, PinsActive, PinDirections, 0x11, (byte)(use4ByteAddress ? 0x04 : 0x03), 0x00,
            use4ByteAddress ? (byte)0x13 : (byte)0x03
        ];
        command = [.. command, .. AddressBytes(address, use4ByteAddress)];
        WriteAll(command);
    }

    private void ClockRead(Span<byte> output)
    {
        // Queue several maximum-size MPSSE reads in one USB write. The FTDI
        // streams their replies while ReadExact drains the receive queue,
        // avoiding one host/device round-trip for every 64 KiB.
        var commandCount = (output.Length + MpsseReadLimit - 1) / MpsseReadLimit;
        Span<byte> commands = stackalloc byte[commandCount * 3];
        var remaining = output.Length;
        for (var i = 0; i < commandCount; i++)
        {
            var chunk = Math.Min(MpsseReadLimit, remaining) - 1;
            commands[i * 3] = 0x20;
            commands[i * 3 + 1] = (byte)chunk;
            commands[i * 3 + 2] = (byte)(chunk >> 8);
            remaining -= chunk + 1;
        }
        WriteAll(commands);
        ReadExact(output, TimeSpan.FromSeconds(Math.Max(5, output.Length / 250_000.0 + 3)));
    }

    private void EndContinuousRead()
    {
        WriteAll([0x80, PinsIdle, PinDirections]);
        Command(0x04);
    }

    private byte ReadStatus() { Span<byte> value = stackalloc byte[1]; Transaction([0x05], value); return value[0]; }
    private void Command(byte opcode) => Transaction([opcode], Span<byte>.Empty);

    private void WakeFlash()
    {
        Command(0x06);
        Span<byte> ignored = stackalloc byte[3];
        Transaction([0xAB,0x00,0x00,0x00], ignored);
        Command(0x04);
        Thread.Sleep(1);
    }

    private void WaitReady(TimeSpan timeout, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + timeout;
        while ((ReadStatus() & 1) != 0)
        {
            token.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("SPI flash remained busy.");
            Thread.Sleep(2);
        }
    }

    private void Transaction(ReadOnlySpan<byte> command, Span<byte> reply)
    {
        if (reply.Length > 65536) throw new ArgumentOutOfRangeException(nameof(reply));
        var mpsse = ArrayPool<byte>.Shared.Rent(command.Length + 16);
        try
        {
            var i = 0; mpsse[i++] = 0x80; mpsse[i++] = PinsActive; mpsse[i++] = PinDirections;
            if (!command.IsEmpty)
            {
                var n = command.Length - 1; mpsse[i++] = 0x11; mpsse[i++] = (byte)n; mpsse[i++] = (byte)(n >> 8);
                command.CopyTo(mpsse.AsSpan(i)); i += command.Length;
            }
            if (!reply.IsEmpty)
            {
                var n = reply.Length - 1; mpsse[i++] = 0x20; mpsse[i++] = (byte)n; mpsse[i++] = (byte)(n >> 8); mpsse[i++] = 0x87;
            }
            mpsse[i++] = 0x80; mpsse[i++] = PinsIdle; mpsse[i++] = PinDirections;
            WriteAll(mpsse.AsSpan(0, i));
            if (!reply.IsEmpty)
            {
                var timeoutSeconds = Math.Max(5, reply.Length / 8_000.0 + 3);
                ReadExact(reply, TimeSpan.FromSeconds(timeoutSeconds));
            }
        }
        finally { ArrayPool<byte>.Shared.Return(mpsse); }
    }

    private unsafe void WriteAll(ReadOnlySpan<byte> data)
    {
        var handle = Handle;
        fixed (byte* p = data)
        {
            var offset = 0; var deadline = DateTime.UtcNow.AddSeconds(2);
            while (offset < data.Length)
            {
                CheckNative(Native.FT_Write(handle, (IntPtr)(p + offset), (uint)(data.Length - offset), out var written), "FT_Write");
                if (written == 0)
                {
                    if (DateTime.UtcNow >= deadline) throw new RT809FException("FT_Write returned zero bytes.", 4);
                    Thread.Sleep(1); continue;
                }
                offset += checked((int)written);
            }
        }
    }

    private unsafe void ReadExact(Span<byte> output, TimeSpan timeout)
    {
        var handle = Handle; var deadline = DateTime.UtcNow + timeout;
        fixed (byte* p = output)
        {
            var offset = 0;
            while (offset < output.Length)
            {
                CheckNative(Native.FT_GetQueueStatus(handle, out var queued), "FT_GetQueueStatus");
                if (queued == 0) { if (DateTime.UtcNow >= deadline) throw new TimeoutException("Timed out waiting for RT809F."); Thread.Sleep(1); continue; }
                var wanted = Math.Min(queued, (uint)(output.Length - offset));
                CheckNative(Native.FT_Read(handle, (IntPtr)(p + offset), wanted, out var read), "FT_Read");
                if (read == 0) throw new RT809FException("FT_Read returned zero bytes.", 4);
                offset += checked((int)read);
            }
        }
    }

    private Lease Enter()
    {
        _gate.Wait();
        try { _ = Handle; return new(_gate); } catch { _gate.Release(); throw; }
    }

    private FtdiHandle Handle => _handle is { IsInvalid: false, IsClosed: false } h ? h : throw new ObjectDisposedException(nameof(RT809FProgrammer));

    private static void Configure(FtdiHandle h)
    {
        CheckNative(Native.FT_ResetDevice(h), "FT_ResetDevice"); CheckNative(Native.FT_SetUSBParameters(h,65536,65536), "FT_SetUSBParameters");
        CheckNative(Native.FT_SetTimeouts(h,5000,5000), "FT_SetTimeouts"); CheckNative(Native.FT_SetLatencyTimer(h,2), "FT_SetLatencyTimer");
        CheckNative(Native.FT_SetBitMode(h,0,0), "FT_SetBitMode(reset)"); CheckNative(Native.FT_SetBitMode(h,0,2), "FT_SetBitMode(MPSSE)");
        Thread.Sleep(50); CheckNative(Native.FT_Purge(h,3), "FT_Purge");
        // RT809F uses the FTDI divide-by-5 clock mode. Divisor 0 selects 6 MHz
        // (12 MHz / ((1 + 0) * 2)), matching the vendor application's clock.
        byte[] init = [0x86,0x00,0x00,0x82,0xFE,0x09,0x80,PinsIdle,PinDirections];
        unsafe { fixed (byte* p = init) CheckNative(Native.FT_Write(h,(IntPtr)p,(uint)init.Length,out var written),"FT_Write(init)"); }
        Thread.Sleep(2);
        CheckNative(Native.FT_Purge(h, 1), "FT_Purge(RX after init)");
    }

    private static void PrepareControlChannelBeforeSpi(string serial)
    {
        RunControlSession(serial, ControlA);
        RunControlSession(serial, ControlA);
        RunControlSession(serial, ControlB, ControlC);
    }

    private static void PrepareControlChannelAfterSpi(string serial)
    {
        RunControlSession(serial, ControlB, ControlD);
        RunControlSession(serial, ControlA);
        RunControlSession(serial, ControlB, ControlC);
    }

    private static void CleanupControlChannel(string serial)
    {
        RunControlSession(serial, ControlB, ControlE);
        RunControlSession(serial, ControlA);
    }

    private static void RunControlSession(string serial, params string[] encodedFrames)
    {
        CheckNative(Native.FT_OpenEx(System.Text.Encoding.ASCII.GetBytes(serial + '\0'), 1, out var raw), "FT_OpenEx(B)");
        using var h = new FtdiHandle(raw);
        CheckNative(Native.FT_ResetDevice(h), "FT_ResetDevice(B)");
        CheckNative(Native.FT_Purge(h, 3), "FT_Purge(B)");
        CheckNative(Native.FT_SetUSBParameters(h, 65536, 65536), "FT_SetUSBParameters(B)");
        CheckNative(Native.FT_SetLatencyTimer(h, 2), "FT_SetLatencyTimer(B)");
        CheckNative(Native.FT_SetBaudRate(h, 100000), "FT_SetBaudRate(B)");
        CheckNative(Native.FT_SetBitMode(h, 0xF5, 0x04), "FT_SetBitMode(sync bit-bang B)");
        Thread.Sleep(2);
        foreach (var encoded in encodedFrames)
        {
            var frame = Convert.FromHexString(encoded);
            unsafe
            {
                fixed (byte* p = frame)
                {
                    CheckNative(Native.FT_Write(h, (IntPtr)p, (uint)frame.Length, out var written), "FT_Write(B)");
                    if (written != frame.Length) throw new RT809FException("Short write on RT809F control channel.", 4);
                }
            }
            Thread.Sleep(2);
        }
        CheckNative(Native.FT_SetBitMode(h, 0, 0), "FT_SetBitMode(reset B)");
    }

    private const string ControlA = "4F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7F6F7FFF";
    private const string ControlB = "0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0F1F0B1B0B1B0F1F0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B5BFF";
    private const string ControlC = "0B1B0B1B0B1B0F1F0B1B0F1F0B1B0B1B0B1B0F1F0B1B0B1B0F1F0B1B0B1B0F1F0B1B0B1B0B1B0F1F0F1F0F1F0F1F0F1F0F1F0F1F0B1B0B1B0B1B0B1B0B1B0B1B5BFF";
    private const string ControlD = "0B1B0B1B0F1F0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0F1F0F1F0F1F0F1F0F1F0B1B0F1F0B1B0B1B0B1B0B1B0B1B0B1B5BFF";
    private const string ControlE = "0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0B1B0F1F0F1F0F1F0F1F0F1F0B1B0F1F0B1B0B1B0B1B0B1B0B1B0B1B5BFF";

    private static void ValidateRange(uint address, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if ((ulong)address + (uint)length > AddressSpaceSize) throw new ArgumentOutOfRangeException(nameof(address), "SPI flash range exceeds 32-bit addressing.");
    }

    private static bool Uses4ByteAddress(uint address) => address > 0xFFFFFF;

    private static bool Uses4ByteAddress(uint address, int length) =>
        address > 0xFFFFFF || (ulong)address + (uint)length > 0x1000000UL;

    private static byte[] AddressBytes(uint address, bool use4ByteAddress) =>
        use4ByteAddress
            ? [(byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address]
            : [(byte)(address >> 16), (byte)(address >> 8), (byte)address];

    private static int ProgressPercent(int done, int total) =>
        total <= 0 ? 100 : (int)Math.Clamp((long)done * 100 / total, 0, 100);

    private static void AppendAddress(List<byte> batch, uint address, bool use4ByteAddress)
    {
        if (use4ByteAddress)
        {
            batch.Add((byte)(address >> 24));
        }

        batch.Add((byte)(address >> 16));
        batch.Add((byte)(address >> 8));
        batch.Add((byte)address);
    }

    private static string Ascii(byte[] value) { var end = Array.IndexOf(value,(byte)0); return System.Text.Encoding.ASCII.GetString(value,0,end < 0 ? value.Length : end); }
    private static void CheckNative(uint status, string operation) { if (status != 0) throw new RT809FException($"{operation} failed with D2XX status {status}.",checked((int)status)); }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_handle is { IsInvalid: false, IsClosed: false } handle)
            {
                Native.FT_SetBitMode(handle, 0, 0);
                Native.FT_Purge(handle, 3);
            }
            _handle?.Dispose();
            _handle = null;
            if (!string.IsNullOrEmpty(_controlSerial))
            {
                CleanupControlChannel(_controlSerial);
                _controlSerial = null;
            }
        }
        finally { _gate.Release(); }
        GC.SuppressFinalize(this);
    }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    private readonly ref struct Lease(SemaphoreSlim gate) { public void Dispose() => gate.Release(); }

    private sealed class FtdiHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public FtdiHandle() : base(true) { }
        public FtdiHandle(IntPtr value) : base(true) => SetHandle(value);
        protected override bool ReleaseHandle() => Native.FT_Close(handle) == 0;
    }

    private static class Native
    {
        private const string Library = "rt809f_d2xx";
        private static readonly object ResolverLock = new();
        private static bool _configured;
        internal static void EnsureLoaded()
        {
            lock (ResolverLock)
            {
                if (_configured) return;
                NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (name, assembly, path) =>
                {
                    if (!string.Equals(name, Library, StringComparison.Ordinal)) return IntPtr.Zero;
                    var preferred = Environment.Is64BitProcess ? "ftd2xx64.dll" : "ftd2xx.dll";
                    if (NativeLibrary.TryLoad(preferred, assembly, path, out var loaded) || NativeLibrary.TryLoad("ftd2xx.dll", assembly, path, out loaded)) return loaded;
                    throw new DllNotFoundException($"Cannot load {preferred}. Copy the matching FTDI D2XX DLL beside the application.");
                });
                _configured = true;
            }
        }
        [DllImport(Library)] internal static extern uint FT_CreateDeviceInfoList(out uint count);
        [DllImport(Library)] internal static extern uint FT_GetDeviceInfoDetail(uint index,out uint flags,out uint type,out uint id,out uint location,[Out] byte[] serial,[Out] byte[] description,out IntPtr handle);
        [DllImport(Library)] internal static extern uint FT_OpenEx(byte[] serial,uint flags,out IntPtr handle);
        [DllImport(Library)] internal static extern uint FT_Close(IntPtr handle);
        [DllImport(Library)] internal static extern uint FT_ResetDevice(FtdiHandle handle);
        [DllImport(Library)] internal static extern uint FT_Purge(FtdiHandle handle,uint mask);
        [DllImport(Library)] internal static extern uint FT_SetTimeouts(FtdiHandle handle,uint read,uint write);
        [DllImport(Library)] internal static extern uint FT_SetLatencyTimer(FtdiHandle handle,byte latency);
        [DllImport(Library)] internal static extern uint FT_SetBaudRate(FtdiHandle handle,uint baudRate);
        [DllImport(Library)] internal static extern uint FT_SetUSBParameters(FtdiHandle handle,uint input,uint output);
        [DllImport(Library)] internal static extern uint FT_SetBitMode(FtdiHandle handle,byte mask,byte mode);
        [DllImport(Library)] internal static extern uint FT_GetQueueStatus(FtdiHandle handle,out uint queued);
        [DllImport(Library)] internal static extern uint FT_Read(FtdiHandle handle,IntPtr buffer,uint size,out uint read);
        [DllImport(Library)] internal static extern uint FT_Write(FtdiHandle handle,IntPtr buffer,uint size,out uint written);
    }
}
