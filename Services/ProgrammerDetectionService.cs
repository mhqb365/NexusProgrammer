using System.Diagnostics;

namespace NexusProgrammer;

public sealed record ProgrammerDetection(
    bool T48Detected,
    bool Rt809fDetected,
    bool Rt809hDetected,
    bool Ch347Detected,
    bool Ch341Detected)
{
    public bool IsConnected(string key) => key switch
    {
        "t48" => T48Detected,
        "rt809f" => Rt809fDetected,
        "rt809h" => Rt809hDetected,
        "ch347" => Ch347Detected,
        "ch341" => Ch341Detected,
        _ => true
    };
}

internal static class ProgrammerDetectionService
{
    public static ProgrammerDetection DetectAvailable()
    {
        var (ch347Detected, ch341Detected) = DetectWchProgrammers();
        return new ProgrammerDetection(
            T48SDKProgrammer.CanOpenDevice(),
            RT809FSDKProgrammer.CanOpenDevice(),
            RT809HSDKProgrammer.CanOpenDevice(),
            ch347Detected,
            ch341Detected);
    }

    private static (bool Ch347Detected, bool Ch341Detected) DetectWchProgrammers()
    {
        var wchUsbDetected = WchUsbDeviceDetector.HasPresentVendor("VID_1A86");
        var ch347UsbDetected = WchUsbDeviceDetector.HasPresentDevice("VID_1A86", "PID_55DA", "PID_55DB", "PID_55DD", "PID_55DE", "PID_55E7");
        var ch341UsbDetected = WchUsbDeviceDetector.HasPresentDevice("VID_1A86", "PID_5512");
        var ch347BackendDetected = Ch347NativeProgrammer.IsAvailable && Ch347NativeProgrammer.CanOpenDevice();
        var ch341BackendDetected = ChNativeProgrammer.IsAvailable && ChNativeProgrammer.CanOpenDevice();

        Debug.WriteLine($"[WCH Detect] USB vendor={wchUsbDetected}, CH341 PID={ch341UsbDetected}, CH347 PID={ch347UsbDetected}");
        Debug.WriteLine($"[WCH Detect] CH341 DLL/backend={ch341BackendDetected}, CH347 DLL/backend={ch347BackendDetected}");

        if (wchUsbDetected)
        {
            var ch347Detected = ch347UsbDetected
                ? ch347BackendDetected
                : !ch341UsbDetected && ch347BackendDetected;
            Debug.WriteLine($"[WCH Detect] Classified CH341={ch341UsbDetected && ch341BackendDetected}, CH347={ch347Detected}");
            return (
                ch347Detected,
                ch341UsbDetected && ch341BackendDetected);
        }

        Debug.WriteLine($"[WCH Detect] SetupAPI WCH not found; fallback CH341={ch341BackendDetected}, CH347={ch347BackendDetected}");
        return (ch347BackendDetected, ch341BackendDetected);
    }
}
