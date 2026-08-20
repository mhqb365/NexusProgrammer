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

public sealed class ProgrammerOption : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isConnected;

    public ProgrammerOption(string key, string name)
    {
        Key = key;
        Name = name;
    }

    public string Key { get; }
    public string Name { get; }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsConnected)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayText)));
            }
        }
    }

    public string DisplayText => Key == "auto" 
        ? Name 
        : $"{Name} ({(IsConnected ? "Connected" : "Not connected")})";

    public override string ToString() => DisplayText;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
