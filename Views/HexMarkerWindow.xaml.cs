using System.Collections.ObjectModel;
using System.Windows;

namespace NexusProgrammer;

public partial class HexMarkerWindow : Window
{
    private readonly ObservableCollection<HexMarker> _markers;
    private bool _formattingHexText;

    public HexMarkerWindow(IEnumerable<HexMarker> markers)
    {
        InitializeComponent();
        _markers = new ObservableCollection<HexMarker>(markers.Select(Clone));
        MarkerList.ItemsSource = _markers;
    }

    public IReadOnlyList<HexMarker> Markers => _markers.ToList();

    private void MarkerList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MarkerList.SelectedItem is not HexMarker marker)
        {
            return;
        }

        NameBox.Text = marker.Name;
        HexBox.Text = marker.Hex;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        MarkerList.SelectedItem = null;
        NameBox.Clear();
        HexBox.Clear();
        NameBox.Focus();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadMarker(out var marker))
        {
            return;
        }

        _markers.Add(marker);
        MarkerList.SelectedItem = marker;
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (MarkerList.SelectedItem is not HexMarker selected)
        {
            MessageBox.Show("Select a marker to update.", "Hex Marker", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryReadMarker(out var marker))
        {
            return;
        }

        selected.Name = marker.Name;
        selected.Hex = marker.Hex;
        MarkerList.Items.Refresh();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (MarkerList.SelectedItem is HexMarker selected)
        {
            _markers.Remove(selected);
        }
    }

    private void HexBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_formattingHexText)
        {
            return;
        }

        var text = HexBox.Text;
        var caretHexDigits = text
            .Take(Math.Clamp(HexBox.CaretIndex, 0, text.Length))
            .Count(Uri.IsHexDigit);
        var formatted = FormatHexInput(text);
        if (formatted == text)
        {
            return;
        }

        _formattingHexText = true;
        try
        {
            HexBox.Text = formatted;
            HexBox.CaretIndex = CaretIndexFromHexDigitCount(formatted, caretHexDigits);
        }
        finally
        {
            _formattingHexText = false;
        }
    }

    private bool TryReadMarker(out HexMarker marker)
    {
        marker = new HexMarker();
        var name = NameBox.Text.Trim();
        var hex = HexBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Marker name is required.", "Hex Marker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!MainWindow.TryParseHexPattern(hex, out var pattern))
        {
            MessageBox.Show("Invalid hex marker.", "Hex Marker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        marker = new HexMarker
        {
            Name = name,
            Hex = MainWindow.FormatHexPattern(pattern)
        };
        return true;
    }

    private static HexMarker Clone(HexMarker marker) => new()
    {
        Name = marker.Name,
        Hex = marker.Hex
    };

    private static string FormatHexInput(string text)
    {
        var hex = new string(text.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return string.Join(" ", Enumerable.Range(0, (hex.Length + 1) / 2)
            .Select(index =>
            {
                var start = index * 2;
                var length = Math.Min(2, hex.Length - start);
                return hex.Substring(start, length);
            }));
    }

    private static int CaretIndexFromHexDigitCount(string formatted, int hexDigitCount)
    {
        if (hexDigitCount <= 0)
        {
            return 0;
        }

        var seen = 0;
        for (var i = 0; i < formatted.Length; i++)
        {
            if (!Uri.IsHexDigit(formatted[i]))
            {
                continue;
            }

            seen++;
            if (seen == hexDigitCount)
            {
                return i + 1;
            }
        }

        return formatted.Length;
    }
}
