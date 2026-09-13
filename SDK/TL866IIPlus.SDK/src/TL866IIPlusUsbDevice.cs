using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TL866IIPlusSdk;

public sealed class TL866IIPlusUsbDevice : IDisposable
{
    private readonly SafeFileHandle _deviceHandle;
    private readonly SafeWinUsbHandle _winUsbHandle;

    private TL866IIPlusUsbDevice(
        TL866IIPlusDeviceInfo info,
        SafeFileHandle deviceHandle,
        SafeWinUsbHandle winUsbHandle,
        IReadOnlyList<UsbPipeInfo> pipes)
    {
        Info = info;
        _deviceHandle = deviceHandle;
        _winUsbHandle = winUsbHandle;
        Pipes = pipes;
    }

    public TL866IIPlusDeviceInfo Info { get; }
    public IReadOnlyList<UsbPipeInfo> Pipes { get; }

    public static TL866IIPlusUsbDevice OpenFirst()
    {
        var info = TL866IIPlusDeviceDiscovery.FindConnectedDevices().FirstOrDefault(device => device.IsTl866IIPlus)
            ?? throw new TL866IIPlusException("No TL866IIPlus WinUSB device was found.");

        return Open(info.DevicePath);
    }

    public static TL866IIPlusUsbDevice Open(string devicePath)
    {
        var file = NativeMethods.CreateFileW(
            devicePath,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            NativeMethods.FileAttributeNormal | NativeMethods.FileFlagOverlapped,
            IntPtr.Zero);

        if (file.IsInvalid)
        {
            throw new TL866IIPlusException("Unable to open TL866IIPlus device.", Marshal.GetLastWin32Error());
        }

        if (!NativeMethods.WinUsb_Initialize(file.DangerousGetHandle(), out var winUsb))
        {
            var error = Marshal.GetLastWin32Error();
            file.Dispose();
            throw new TL866IIPlusException("Unable to initialize WinUSB for TL866IIPlus device.", error);
        }

        try
        {
            var descriptor = ReadDeviceDescriptor(winUsb);
            var product = ReadStringDescriptor(winUsb, descriptor.ProductIndex);
            var info = new TL866IIPlusDeviceInfo(
                devicePath,
                descriptor.VendorId,
                descriptor.ProductId,
                TL866IIPlusDeviceInfo.WinUsbInterfaceGuid,
                product);

            return new TL866IIPlusUsbDevice(info, file, winUsb, QueryPipes(winUsb));
        }
        catch
        {
            winUsb.Dispose();
            file.Dispose();
            throw;
        }
    }

    private static IReadOnlyList<UsbPipeInfo> QueryPipes(SafeWinUsbHandle winUsb)
    {
        if (!NativeMethods.WinUsb_QueryInterfaceSettings(winUsb, 0, out var descriptor))
        {
            throw new TL866IIPlusException("Unable to query TL866IIPlus USB interface.", Marshal.GetLastWin32Error());
        }

        var pipes = new List<UsbPipeInfo>();
        for (byte i = 0; i < descriptor.NumEndpoints; i++)
        {
            if (!NativeMethods.WinUsb_QueryPipe(winUsb, 0, i, out var pipe))
            {
                throw new TL866IIPlusException("Unable to query TL866IIPlus USB pipe.", Marshal.GetLastWin32Error());
            }

            pipes.Add(new UsbPipeInfo(pipe.PipeId, pipe.PipeType, pipe.MaximumPacketSize, pipe.Interval));
        }

        return pipes;
    }

    private static UsbDeviceDescriptor ReadDeviceDescriptor(SafeWinUsbHandle winUsb)
    {
        var buffer = new byte[Marshal.SizeOf<UsbDeviceDescriptor>()];
        if (!NativeMethods.WinUsb_GetDescriptor(
            winUsb,
            NativeMethods.UsbDeviceDescriptorType,
            0,
            0,
            buffer,
            (uint)buffer.Length,
            out var transferred) ||
            transferred < buffer.Length)
        {
            throw new TL866IIPlusException("Unable to read TL866IIPlus device descriptor.", Marshal.GetLastWin32Error());
        }

        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<UsbDeviceDescriptor>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    private static string ReadStringDescriptor(SafeWinUsbHandle winUsb, byte index)
    {
        if (index == 0)
        {
            return string.Empty;
        }

        var buffer = new byte[255];
        if (!NativeMethods.WinUsb_GetDescriptor(
            winUsb,
            NativeMethods.UsbStringDescriptorType,
            index,
            0x0409,
            buffer,
            (uint)buffer.Length,
            out var transferred) ||
            transferred < 2)
        {
            return string.Empty;
        }

        var byteCount = Math.Min(buffer[0], (byte)transferred) - 2;
        return byteCount <= 0 ? string.Empty : Encoding.Unicode.GetString(buffer, 2, byteCount);
    }

    public void Dispose()
    {
        _winUsbHandle.Dispose();
        _deviceHandle.Dispose();
    }
}
