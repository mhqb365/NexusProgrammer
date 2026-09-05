using System.Collections.ObjectModel;
using System.Windows;

namespace NexusProgrammer;

public partial class FillSelectionWindow : Window
{
    private readonly ObservableCollection<HexFillPreset> _presets;
    private bool _formattingHexText;

    public FillSelectionWindow(IEnumerable<HexFillPreset> presets)
    {
        InitializeComponent();
        _presets = new ObservableCollection<HexFillPreset>(presets.Select(Clone));
        PresetList.ItemsSource = _presets;
    }

    public IReadOnlyList<HexFillPreset> Presets => _presets.ToList();
    public byte[] FillPattern { get; private set; } = [];

    private void PresetList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PresetList.SelectedItem is not HexFillPreset preset)
        {
            return;
        }

        NameBox.Text = preset.Name;
        HexBox.Text = preset.Hex;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadPreset(out var preset, requireName: true))
        {
            return;
        }

        _presets.Add(preset);
        PresetList.SelectedItem = preset;
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not HexFillPreset selected)
        {
            MessageBox.Show("Select a preset to update.", "Fill Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryReadPreset(out var preset, requireName: true))
        {
            return;
        }

        selected.Name = preset.Name;
        selected.Hex = preset.Hex;
        PresetList.Items.Refresh();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is HexFillPreset selected)
        {
            _presets.Remove(selected);
        }
    }

    private void Fill_Click(object sender, RoutedEventArgs e)
    {
        var hex = HexBox.Text.Trim();
        if (!HexSearchService.TryParseHexPattern(hex, out var pattern))
        {
            MessageBox.Show("Invalid fill hex.", "Fill Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FillPattern = pattern;
        DialogResult = true;
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

    private bool TryReadPreset(out HexFillPreset preset, bool requireName)
    {
        preset = new HexFillPreset();
        var name = NameBox.Text.Trim();
        var hex = HexBox.Text.Trim();

        if (requireName && string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Preset name is required.", "Fill Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!HexSearchService.TryParseHexPattern(hex, out var pattern))
        {
            MessageBox.Show("Invalid fill hex.", "Fill Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        preset = new HexFillPreset
        {
            Name = name,
            Hex = HexSearchService.FormatHexPattern(pattern)
        };
        return true;
    }

    private static HexFillPreset Clone(HexFillPreset preset) => new()
    {
        Name = preset.Name,
        Hex = preset.Hex
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
