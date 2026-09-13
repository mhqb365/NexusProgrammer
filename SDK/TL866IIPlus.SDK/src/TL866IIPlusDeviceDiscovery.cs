using System.Runtime.InteropServices;

namespace TL866IIPlusSdk;

public static class TL866IIPlusDeviceDiscovery
{
    public static IReadOnlyList<TL866IIPlusDeviceInfo> FindConnectedDevices()
    {
        var guid = TL866IIPlusDeviceInfo.WinUsbInterfaceGuid;
        var set = NativeMethods.SetupDiGetClassDevsW(
            in guid,
            null,
            IntPtr.Zero,
            NativeMethods.DigcfPresent | NativeMethods.DigcfDeviceInterface);

        if (set == IntPtr.Zero || set == new IntPtr(-1))
        {
            throw new TL866IIPlusException("Unable to get XGecu device interface list.", Marshal.GetLastWin32Error());
        }

        try
        {
            var devices = new List<TL866IIPlusDeviceInfo>();
            for (uint i = 0; ; i++)
            {
                var data = new SpDeviceInterfaceData
                {
                    CbSize = Marshal.SizeOf<SpDeviceInterfaceData>()
                };

                if (!NativeMethods.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, in guid, i, ref data))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ErrorNoMoreItems)
                    {
                        break;
                    }

                    throw new TL866IIPlusException("Unable to enumerate XGecu device interfaces.", error);
                }

                var path = ReadDevicePath(set, ref data);
                if (string.IsNullOrWhiteSpace(path) || !LooksLikeXGecuWinUsbPath(path))
                {
                    continue;
                }

                try
                {
                    using var device = TL866IIPlusUsbDevice.Open(path);
                    devices.Add(device.Info);
                }
                catch
                {
                    // Ignore interfaces which belong to other XGecu devices or are busy.
                }
            }

            return devices;
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string? ReadDevicePath(IntPtr set, ref SpDeviceInterfaceData data)
    {
        NativeMethods.SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
        var detail = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, size, out _, IntPtr.Zero))
            {
                throw new TL866IIPlusException("Unable to read XGecu device path.", Marshal.GetLastWin32Error());
            }

            return Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }

    private static bool LooksLikeXGecuWinUsbPath(string path) =>
        path.Contains("vid_a466", StringComparison.OrdinalIgnoreCase) &&
        path.Contains("pid_0a53", StringComparison.OrdinalIgnoreCase);
}
