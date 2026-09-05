namespace NexusProgrammer;

public sealed record ProgrammerSelection(string Key, string StatusText, bool IsConnected)
{
    public IChipProgrammer CreateProgrammer() => Key switch
    {
        "ch341" => new ChNativeProgrammer(),
        "ch347" => new Ch347NativeProgrammer(),
        "rt809f" => new RT809FSDKProgrammer(),
        "rt809h" => new RT809HSDKProgrammer(),
        "t48" => new T48SDKProgrammer(),
        _ => new MockProgrammer()
    };
}

public static class ProgrammerWorkflowService
{
    public static ProgrammerSelection ResolveSelection(string selectedMode, ProgrammerDetection detection)
    {
        selectedMode = string.IsNullOrWhiteSpace(selectedMode) ? "auto" : selectedMode;
        if (selectedMode == "auto")
        {
            if (detection.Ch341Detected)
            {
                return Connected("ch341");
            }

            if (detection.Ch347Detected)
            {
                return Connected("ch347");
            }

            if (detection.Rt809fDetected)
            {
                return Connected("rt809f");
            }

            if (detection.Rt809hDetected)
            {
                return Connected("rt809h");
            }

            if (detection.T48Detected)
            {
                return Connected("t48");
            }

            return new ProgrammerSelection("none", "Programmer disconnected", IsConnected: false);
        }

        return detection.IsConnected(selectedMode)
            ? Connected(selectedMode)
            : new ProgrammerSelection("none", $"{DisplayName(selectedMode)} disconnected", IsConnected: false);
    }

    public static string DisplayName(string key) => key switch
    {
        "ch341" => "CH341",
        "ch347" => "CH347",
        "rt809f" => "RT809F",
        "rt809h" => "RT809H",
        "t48" => "XGecu T48",
        _ => "Programmer"
    };

    private static ProgrammerSelection Connected(string key) =>
        new(key, $"{DisplayName(key)} connected", IsConnected: true);
}
