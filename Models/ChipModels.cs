namespace NexusProgrammer;

public sealed record ChipProfile(
    string Name,
    string Protocol,
    int SizeBytes,
    int PageSize,
    string CommandSet,
    string Manufacturer = "GENERIC",
    string Volts = "",
    string Type = "");

public sealed record IcCandidate(
    string Device,
    string Volts,
    string Size,
    string Page,
    string Manuf,
    string Type,
    ChipProfile Profile,
    string JedecId,
    bool IsUserAdded = false);

public sealed record SizeOption(string Label, int Bytes);

public sealed record SearchResult(bool Found, int Offset, int Length, string Message)
{
    public static SearchResult Success(int offset, int length) => new(true, offset, length, string.Empty);

    public static SearchResult Fail(string message) => new(false, -1, 0, message);
}

public sealed record SearchHit(int Offset, int Length, string OffsetText, string HexExcerpt, string TextExcerpt)
{
    public static SearchHit Message(string message) => new(-1, 0, string.Empty, message, string.Empty);
}

public sealed record WindowsKeyCandidate(string Method, int Offset, string Key, int Length, string Description);


