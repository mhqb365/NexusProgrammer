using System.Globalization;
using System.Windows;

namespace NexusProgrammer;

public partial class SplitBiosWindow : Window
{
    public SplitBiosWindow(IEnumerable<MemoryBufferOption> memoryTabs, string? selectedLabel = null)
    {
        InitializeComponent();
        var options = memoryTabs.ToList();
        BiosCombo.ItemsSource = options;
        BiosCombo.DisplayMemberPath = nameof(MemoryBufferOption.Label);
        var selectedIndex = !string.IsNullOrWhiteSpace(selectedLabel)
            ? options.FindIndex(option => option.Label.Equals(selectedLabel, StringComparison.OrdinalIgnoreCase))
            : -1;
        BiosCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : options.Count > 0 ? 0 : -1;
    }

    public MemoryBufferOption? Bios => BiosCombo.SelectedItem as MemoryBufferOption;

    public int File1Length { get; private set; }

    public int File2Length { get; private set; }

    private void Split_Click(object sender, RoutedEventArgs e)
    {
        if (Bios is null)
        {
            MessageBox.Show(this, "Select a Memory tab first.", "Split BIOS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParseSize(File1SizeBox.Text, out var file1Length) ||
            !TryParseSize(File2SizeBox.Text, out var file2Length))
        {
            MessageBox.Show(this, "Enter valid sizes. Examples: 8, 8MB, 8192KB, 0x800000.", "Split BIOS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (file1Length <= 0 || file2Length <= 0 || file1Length + file2Length > Bios.Buffer.Length)
        {
            MessageBox.Show(this, $"Split sizes must be positive and no larger than selected BIOS size ({FormatBytes(Bios.Buffer.Length)}).", "Split BIOS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        File1Length = file1Length;
        File2Length = file2Length;
        DialogResult = true;
    }

    private static bool TryParseSize(string text, out int bytes)
    {
        bytes = 0;
        var value = text.Trim().Replace(" ", "", StringComparison.OrdinalIgnoreCase);
        if (value.Length == 0)
        {
            return false;
        }

        var multiplier = 1024 * 1024L;
        if (value.EndsWith("MB", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^2];
        }
        else if (value.EndsWith("M", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^1];
        }
        else if (value.EndsWith("KB", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^2];
            multiplier = 1024;
        }
        else if (value.EndsWith("K", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^1];
            multiplier = 1024;
        }
        else if (value.EndsWith("B", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^1];
            multiplier = 1;
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexBytes))
            {
                return false;
            }

            bytes = hexBytes is > 0 and <= int.MaxValue ? (int)hexBytes : 0;
            return bytes > 0;
        }

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        var result = number * multiplier;
        if (result <= 0 || result > int.MaxValue || result != decimal.Truncate(result))
        {
            return false;
        }

        bytes = (int)result;
        return true;
    }

    private static string FormatBytes(int bytes) =>
        bytes % (1024 * 1024) == 0
            ? $"{bytes / 1024 / 1024} MB"
            : $"{bytes:N0} bytes";
}
