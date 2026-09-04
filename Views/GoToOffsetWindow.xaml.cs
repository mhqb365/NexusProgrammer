using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace NexusProgrammer;

public partial class GoToOffsetWindow : Window
{
    private readonly int _bufferLength;

    public GoToOffsetWindow(int bufferLength)
    {
        InitializeComponent();
        _bufferLength = bufferLength;
        OffsetBox.Focus();
        OffsetBox.SelectAll();
    }

    public int TargetOffset { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveOffset(out var offset))
        {
            MessageBox.Show("Invalid offset.", "Go to", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if ((uint)offset >= _bufferLength)
        {
            MessageBox.Show($"Offset is outside buffer range 0x000000-0x{Math.Max(0, _bufferLength - 1):X6}.", "Go to", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TargetOffset = offset;
        DialogResult = true;
    }

    private void OffsetBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Ok_Click(sender, e);
        }
    }

    private bool TryResolveOffset(out int offset)
    {
        offset = 0;
        var text = OffsetBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offset))
        {
            return false;
        }

        return true;
    }
}
