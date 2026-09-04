using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NexusProgrammer;

public partial class HexSearchWindow : Window
{
    private readonly Func<string, string, bool, Task<bool>> _searchAsync;
    private readonly Func<string, string, Task<bool>> _searchAllAsync;
    private bool _formattingHexText;

    public HexSearchWindow(
        string mode,
        string query,
        Func<string, string, bool, Task<bool>> searchAsync,
        Func<string, string, Task<bool>> searchAllAsync)
    {
        InitializeComponent();
        _searchAsync = searchAsync;
        _searchAllAsync = searchAllAsync;
        SelectMode(string.IsNullOrWhiteSpace(mode) ? "Text" : mode);
        CurrentQueryBox().Text = query;
        FocusQueryBox(selectAll: true);
        UpdateModeButtons();
    }

    private async void Previous_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => _searchAsync(CurrentMode(), CurrentQuery(), false));

    private async void Next_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => _searchAsync(CurrentMode(), CurrentQuery(), true));

    private async void All_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => _searchAllAsync(CurrentMode(), CurrentQuery()));

    private async void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await RunAsync(() => _searchAsync(CurrentMode(), CurrentQuery(), true));
    }

    private void ModeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, ModeTabs))
        {
            return;
        }

        UpdateModeButtons();
        FocusQueryBox(selectAll: false);
    }

    private void HexQueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formattingHexText)
        {
            return;
        }

        var text = HexQueryBox.Text;
        var caretHexDigits = text
            .Take(Math.Clamp(HexQueryBox.CaretIndex, 0, text.Length))
            .Count(Uri.IsHexDigit);
        var formatted = FormatHexInput(text);
        if (formatted == text)
        {
            return;
        }

        _formattingHexText = true;
        try
        {
            HexQueryBox.Text = formatted;
            HexQueryBox.CaretIndex = CaretIndexFromHexDigitCount(formatted, caretHexDigits);
        }
        finally
        {
            _formattingHexText = false;
        }
    }

    private async Task RunAsync(Func<Task<bool>> action)
    {
        SetButtonsEnabled(false);
        try
        {
            if (await action())
            {
                Close();
                return;
            }
        }
        finally
        {
            SetButtonsEnabled(true);
            FocusQueryBox(selectAll: false);
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

        ModeTabs.SelectedIndex = 0;
    }

    private string CurrentMode() => (ModeTabs.SelectedItem as TabItem)?.Tag as string ?? "Text";

    private string CurrentQuery() => CurrentQueryBox().Text;

    private TextBox CurrentQueryBox() => CurrentMode() switch
    {
        "Hex" => HexQueryBox,
        _ => TextQueryBox
    };

    private void FocusQueryBox(bool selectAll)
    {
        var box = CurrentQueryBox();
        box.Focus();
        if (selectAll)
        {
            box.SelectAll();
        }
    }

    private void UpdateModeButtons()
    {
    }

    private void SetButtonsEnabled(bool enabled)
    {
        PreviousButton.IsEnabled = enabled;
        NextButton.IsEnabled = enabled;
        AllButton.IsEnabled = enabled;
    }

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
