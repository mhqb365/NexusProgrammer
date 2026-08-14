using System.Windows;

namespace NexusProgrammer;

public partial class AddIcWindow : Window
{
    public AddIcWindow(string? jedecId = null, IcCandidate? candidate = null)
    {
        InitializeComponent();
        if (candidate is not null)
        {
            Title = "Edit IC";
            SaveButton.Content = "Save";
            DeviceBox.Text = candidate.Device;
            ManufacturerBox.Text = candidate.Manuf;
            JedecBox.Text = candidate.JedecId;
            CapacityBox.Text = FormatCapacity(candidate.Profile.SizeBytes);
            PageBox.Text = candidate.Profile.PageSize.ToString();
            VoltsBox.Text = candidate.Volts.TrimEnd('V');
        }
        else if (!string.IsNullOrWhiteSpace(jedecId))
        {
            JedecBox.Text = jedecId;
        }
    }

    public IcCandidate? Candidate { get; private set; }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var device = DeviceBox.Text.Trim();
        var manufacturer = ManufacturerBox.Text.Trim();
        var jedecId = NormalizeJedecId(JedecBox.Text);
        if (string.IsNullOrWhiteSpace(device))
        {
            ShowError("Device is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(jedecId))
        {
            ShowError("JEDEC ID is required.");
            return;
        }

        if (!TryParseCapacity(CapacityBox.Text, out var sizeBytes))
        {
            ShowError("Capacity must be like 8MB, 16MB, or 128Mbit.");
            return;
        }

        if (!int.TryParse(PageBox.Text.Trim(), out var pageSize) || pageSize <= 0)
        {
            ShowError("Page bytes must be a positive number.");
            return;
        }

        if (string.IsNullOrWhiteSpace(manufacturer))
        {
            manufacturer = "GENERIC";
        }

        var volts = VoltsBox.Text.Trim();
        var profile = new ChipProfile(device, "SPI", sizeBytes, pageSize, "25xx", manufacturer, volts, "SPI_NOR");
        Candidate = new IcCandidate(
            device,
            volts,
            IcCatalogLoader.FormatMbits(sizeBytes),
            $"{pageSize} Bytes",
            manufacturer,
            "SPI_NOR",
            profile,
            jedecId);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
    }

    private static string NormalizeJedecId(string value)
    {
        var hex = new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return string.Join(" ", Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)));
    }

    private static bool TryParseCapacity(string text, out int sizeBytes)
    {
        sizeBytes = 0;
        var value = text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var unit = "MB";
        if (value.EndsWith("MBIT", StringComparison.Ordinal))
        {
            unit = "MBIT";
            value = value[..^4];
        }
        else if (value.EndsWith("MIB", StringComparison.Ordinal))
        {
            unit = "MB";
            value = value[..^3];
        }
        else if (value.EndsWith("MB", StringComparison.Ordinal))
        {
            value = value[..^2];
        }
        else if (value.EndsWith("M", StringComparison.Ordinal))
        {
            unit = "MBIT";
            value = value[..^1];
        }

        if (!double.TryParse(value, out var number) || number <= 0)
        {
            return false;
        }

        var bytes = unit == "MBIT"
            ? number * 1024 * 1024 / 8
            : number * 1024 * 1024;
        if (bytes > int.MaxValue)
        {
            return false;
        }

        sizeBytes = (int)Math.Round(bytes);
        return sizeBytes > 0;
    }

    private static string FormatCapacity(int sizeBytes)
    {
        return sizeBytes % (1024 * 1024) == 0
            ? $"{sizeBytes / (1024 * 1024)}MB"
            : IcCatalogLoader.FormatMbits(sizeBytes);
    }
}
