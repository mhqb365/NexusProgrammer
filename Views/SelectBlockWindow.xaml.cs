using System.Globalization;
using System.Windows;

namespace NexusProgrammer;

public partial class SelectBlockWindow : Window
{
    private readonly int _bufferLength;

    public SelectBlockWindow(int bufferLength, int selectedOffset, int selectionLength)
    {
        InitializeComponent();
        _bufferLength = bufferLength;
        var start = Math.Clamp(selectedOffset, 0, Math.Max(0, bufferLength - 1));
        var length = Math.Max(1, selectionLength);
        StartBox.Text = start.ToString("X", CultureInfo.InvariantCulture);
        EndBox.Text = Math.Min(bufferLength - 1, start + length - 1).ToString("X", CultureInfo.InvariantCulture);
        LengthBox.Text = length.ToString("X", CultureInfo.InvariantCulture);
        StartBox.Focus();
        StartBox.SelectAll();
    }

    public int StartOffset { get; private set; }

    public int BlockLength { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseHex(StartBox.Text, out var start) || (uint)start >= _bufferLength)
        {
            MessageBox.Show($"Start offset is outside buffer range 0x0-0x{Math.Max(0, _bufferLength - 1):X}.", "Select block", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int length;
        if (LengthRadio.IsChecked == true)
        {
            if (!TryParseHex(LengthBox.Text, out length) || length <= 0)
            {
                MessageBox.Show("Invalid length.", "Select block", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else
        {
            if (!TryParseHex(EndBox.Text, out var end) || end < start || (uint)end >= _bufferLength)
            {
                MessageBox.Show("Invalid end offset.", "Select block", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            length = end - start + 1;
        }

        if (start + length > _bufferLength)
        {
            MessageBox.Show("Selected block is outside buffer range.", "Select block", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StartOffset = start;
        BlockLength = length;
        DialogResult = true;
    }

    private static bool TryParseHex(string text, out int value)
    {
        value = 0;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return text.Length > 0 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
