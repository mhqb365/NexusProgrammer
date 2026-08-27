namespace NexusProgrammer;

internal sealed record ProgrammerDetection(
    bool T48Detected,
    bool Rt809fDetected,
    bool Ch347Detected,
    bool Ch341Detected)
{
    public bool IsConnected(string key) => key switch
    {
        "t48" => T48Detected,
        "rt809f" => Rt809fDetected,
        "ch347" => Ch347Detected,
        "ch341" => Ch341Detected,
        _ => true
    };
}

internal static class ProgrammerDetectionService
{
    public static ProgrammerDetection DetectAvailable() => new(
        T48SDKProgrammer.CanOpenDevice(),
        RT809FSDKProgrammer.CanOpenDevice(),
        Ch347NativeProgrammer.IsAvailable && Ch347NativeProgrammer.CanOpenDevice(),
        ChNativeProgrammer.IsAvailable && ChNativeProgrammer.CanOpenDevice());
}
