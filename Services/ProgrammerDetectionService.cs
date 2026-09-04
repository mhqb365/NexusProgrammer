namespace NexusProgrammer;

internal sealed record ProgrammerDetection(
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
        var ch347UsbDetected = WchUsbDeviceDetector.HasPresentDevice("VID_1A86", "PID_55DA", "PID_55DB");
        var ch341UsbDetected = WchUsbDeviceDetector.HasPresentDevice("VID_1A86", "PID_5512");
        if (ch347UsbDetected || ch341UsbDetected)
        {
            return (
                ch347UsbDetected && Ch347NativeProgrammer.IsAvailable && Ch347NativeProgrammer.CanOpenDevice(),
                ch341UsbDetected && ChNativeProgrammer.IsAvailable && ChNativeProgrammer.CanOpenDevice());
        }

        var ch347Fallback = Ch347NativeProgrammer.IsAvailable && Ch347NativeProgrammer.CanOpenDevice();
        var ch341Fallback = !ch347Fallback && ChNativeProgrammer.IsAvailable && ChNativeProgrammer.CanOpenDevice();
        return (ch347Fallback, ch341Fallback);
    }
}
