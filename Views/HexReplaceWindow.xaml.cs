using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NexusProgrammer;

public partial class HexReplaceWindow : Window
{
    private readonly Func<string, string, string, bool, bool, Task> _replaceAsync;
    private bool _formattingHexText;

    public HexReplaceWindow(string mode, string query, Func<string, string, string, bool, bool, Task> replaceAsync)
    {
        InitializeComponent();
        _replaceAsync = replaceAsync;
        SelectMode(string.Equals(mode, "Hex", StringComparison.OrdinalIgnoreCase) ? "Hex" : "Text");
        CurrentSearchBox().Text = query;
        CurrentSearchBox().Focus();
        CurrentSearchBox().SelectAll();
    }

    private async void Previous_Click(object sender, RoutedEventArgs e) => await RunReplaceAsync(replaceAll: false, forward: false);

    private async void Next_Click(object sender, RoutedEventArgs e) => await RunReplaceAsync(replaceAll: false, forward: true);

    private async void ReplaceAll_Click(object sender, RoutedEventArgs e) => await RunReplaceAsync(replaceAll: true, forward: true);

    private async void ReplaceBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await RunReplaceAsync(replaceAll: false, forward: true);
    }

    private void ModeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, ModeTabs))
        {
            CurrentSearchBox().Focus();
        }
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formattingHexText || sender is not TextBox box)
        {
            return;
        }

        var text = box.Text;
        var caretHexDigits = text
            .Take(Math.Clamp(box.CaretIndex, 0, text.Length))
            .Count(Uri.IsHexDigit);
        var formatted = FormatHexInput(text);
        if (formatted == text)
        {
            return;
        }

        _formattingHexText = true;
        try
        {
            box.Text = formatted;
            box.CaretIndex = CaretIndexFromHexDigitCount(formatted, caretHexDigits);
        }
        finally
        {
            _formattingHexText = false;
        }
    }

    private async Task RunReplaceAsync(bool replaceAll, bool forward)
    {
        PreviousButton.IsEnabled = false;
        NextButton.IsEnabled = false;
        ReplaceAllButton.IsEnabled = false;
        try
        {
            await _replaceAsync(
                CurrentMode(),
                CurrentSearchBox().Text,
                CurrentReplaceBox().Text,
                replaceAll,
                forward);
        }
        finally
        {
            PreviousButton.IsEnabled = true;
            NextButton.IsEnabled = true;
            ReplaceAllButton.IsEnabled = true;
            CurrentReplaceBox().Focus();
        }
    }

    private void SelectMode(string mode)
    {
        foreach (TabItem tab in ModeTabs.Items)
        {
            if (string.Equals(tab.Tag as string, mode, StringComparison.OrdinalIgnoreCase))
            {
                ModeTabs.SelectedItem = tab;
                return;
            }
        }
    }

    private string CurrentMode() => (ModeTabs.SelectedItem as TabItem)?.Tag as string ?? "Text";

    private TextBox CurrentSearchBox() => CurrentMode() == "Hex" ? HexSearchBox : TextSearchBox;

    private TextBox CurrentReplaceBox() => CurrentMode() == "Hex" ? HexReplaceBox : TextReplaceBox;

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
