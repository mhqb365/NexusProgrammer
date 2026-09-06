using System.Windows;
using System.Windows.Controls.Primitives;
using System.Text;
using System.Windows.Input;

namespace NexusProgrammer;

public partial class HexCompareWindow : Window
{
    private readonly MemoryBufferOption _first;
    private readonly MemoryBufferOption _second;
    private readonly HexCompareResult _result;
    private bool _updatingScrollBar;
    private int _currentOffset;

    public HexCompareWindow(MemoryBufferOption first, MemoryBufferOption second)
    {
        InitializeComponent();
        _first = first;
        _second = second;
        _result = HexCompareService.Compare(first.Buffer, second.Buffer);
        FirstHeader = $"{first.Label} - {DisplayName(first)}";
        SecondHeader = $"{second.Label} - {DisplayName(second)}";
        DataContext = this;
        FirstView.SetData(first.Buffer, _result.DifferenceOffsets);
        SecondView.SetData(second.Buffer, _result.DifferenceOffsets);
        FirstView.ScrollChanged += (_, _) => SyncFromView(FirstView);
        SecondView.ScrollChanged += (_, _) => SyncFromView(SecondView);
        FirstView.OffsetClicked += (_, offset) => SelectOffset(offset);
        SecondView.OffsetClicked += (_, offset) => SelectOffset(offset);
        FirstView.SelectionChanged += (_, _) => MirrorSelection(FirstView, SecondView);
        SecondView.SelectionChanged += (_, _) => MirrorSelection(SecondView, FirstView);
        Loaded += (_, _) => UpdateScrollBar();
        UpdateStatus();
    }

    public string FirstHeader { get; }
    public string SecondHeader { get; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        if (e.Key == Key.F)
        {
            e.Handled = true;
            Search_Click(this, e);
            return;
        }

        if (e.Key == Key.G)
        {
            e.Handled = true;
            GoTo_Click(this, e);
        }
    }

    private void PreviousDiff_Click(object sender, RoutedEventArgs e)
    {
        var offset = HexCompareService.FindPreviousDifference(_result, _currentOffset);
        if (offset >= 0)
        {
            ScrollToOffset(offset);
        }
    }

    private void NextDiff_Click(object sender, RoutedEventArgs e)
    {
        var offset = HexCompareService.FindNextDifference(_result, _currentOffset);
        if (offset >= 0)
        {
            ScrollToOffset(offset);
        }
    }

    private void FirstDiff_Click(object sender, RoutedEventArgs e)
    {
        if (_result.DifferenceOffsets.Count > 0)
        {
            ScrollToOffset(_result.DifferenceOffsets[0]);
        }
    }

    private void LastDiff_Click(object sender, RoutedEventArgs e)
    {
        if (_result.DifferenceOffsets.Count > 0)
        {
            ScrollToOffset(_result.DifferenceOffsets[^1]);
        }
    }

    private void FirstEqual_Click(object sender, RoutedEventArgs e) => ScrollToFoundOffset(HexCompareService.FindFirstEqual(_result));

    private void PreviousEqual_Click(object sender, RoutedEventArgs e) => ScrollToFoundOffset(HexCompareService.FindPreviousEqual(_result, _currentOffset));

    private void NextEqual_Click(object sender, RoutedEventArgs e) => ScrollToFoundOffset(HexCompareService.FindNextEqual(_result, _currentOffset));

    private void LastEqual_Click(object sender, RoutedEventArgs e) => ScrollToFoundOffset(HexCompareService.FindLastEqual(_result));

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HexSearchWindow("Hex", string.Empty, SearchAsync, SearchAllAsync, showAllButton: false, closeOnSuccess: false)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void GoTo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new GoToOffsetWindow(_result.Length)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            ScrollToOffset(dialog.TargetOffset);
        }
    }

    private Task<bool> SearchAsync(string mode, string query, bool forward)
    {
        if (!TryBuildSearchPattern(mode, query, out var pattern, out var asciiText))
        {
            return Task.FromResult(false);
        }

        var offset = FindCompareMatch(pattern, asciiText, forward);
        if (offset < 0)
        {
            MessageBox.Show(this, "Search pattern not found.", "Hex Compare", MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.FromResult(false);
        }

        ScrollToOffset(offset);
        return Task.FromResult(true);
    }

    private Task<bool> SearchAllAsync(string mode, string query)
    {
        if (!TryBuildSearchPattern(mode, query, out var pattern, out var asciiText))
        {
            return Task.FromResult(false);
        }

        var offset = FindCompareMatches(pattern, asciiText).FirstOrDefault(-1);
        if (offset < 0)
        {
            MessageBox.Show(this, "Search pattern not found.", "Hex Compare", MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.FromResult(false);
        }

        ScrollToOffset(offset);
        return Task.FromResult(true);
    }

    private void ScrollToFoundOffset(int offset)
    {
        if (offset >= 0)
        {
            ScrollToOffset(offset);
        }
    }

    private bool TryBuildSearchPattern(string mode, string query, out byte[] pattern, out bool asciiText)
    {
        asciiText = false;
        if (string.Equals(mode, "Hex", StringComparison.OrdinalIgnoreCase))
        {
            if (HexSearchService.TryParseHexPattern(query, out pattern))
            {
                return true;
            }

            MessageBox.Show(this, "Invalid hex pattern.", "Hex Compare", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!string.IsNullOrEmpty(query))
        {
            pattern = Encoding.ASCII.GetBytes(query);
            asciiText = true;
            return true;
        }

        pattern = [];
        MessageBox.Show(this, "Search text is empty.", "Hex Compare", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private int FindCompareMatch(byte[] pattern, bool asciiText, bool forward)
    {
        var matches = FindCompareMatches(pattern, asciiText);
        if (matches.Count == 0)
        {
            return -1;
        }

        if (forward)
        {
            return matches.FirstOrDefault(offset => offset > _currentOffset, matches[0]);
        }

        for (var i = matches.Count - 1; i >= 0; i--)
        {
            if (matches[i] < _currentOffset)
            {
                return matches[i];
            }
        }

        return matches[^1];
    }

    private List<int> FindCompareMatches(byte[] pattern, bool asciiText)
    {
        var matches = asciiText
            ? HexSearchService.FindAllAsciiText(_first.Buffer, pattern)
            : HexSearchService.FindAllBytes(_first.Buffer, pattern);
        matches.AddRange(asciiText
            ? HexSearchService.FindAllAsciiText(_second.Buffer, pattern)
            : HexSearchService.FindAllBytes(_second.Buffer, pattern));
        return matches.Distinct().Order().ToList();
    }

    private void CompareScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingScrollBar)
        {
            return;
        }

        var line = (int)e.NewValue;
        FirstView.SetFirstLine(line);
        SecondView.SetFirstLine(line);
    }

    private void SyncFromView(HexCompareView source)
    {
        if (_updatingScrollBar)
        {
            return;
        }

        FirstView.SetFirstLine(source.FirstLine);
        SecondView.SetFirstLine(source.FirstLine);
        UpdateScrollBar();
    }

    private void ScrollToOffset(int offset)
    {
        _currentOffset = offset;
        FirstView.ScrollToOffset(offset);
        SecondView.ScrollToOffset(offset);
        UpdateScrollBar();
        UpdateStatus();
    }

    private void SelectOffset(int offset)
    {
        _currentOffset = offset;
        FirstView.SetCurrentOffset(offset);
        SecondView.SetCurrentOffset(offset);
        UpdateStatus();
    }

    private static void MirrorSelection(HexCompareView source, HexCompareView target)
    {
        target.ClearSelection();
        if (source.SelectionLength <= 1)
        {
            target.ClearMirrorSelection();
            return;
        }

        target.SetMirrorSelection(source.SelectionStart, source.SelectionLength);
    }

    private void UpdateScrollBar()
    {
        _updatingScrollBar = true;
        try
        {
            CompareScrollBar.Maximum = Math.Max(0, Math.Max(FirstView.TotalLines, SecondView.TotalLines) - Math.Min(FirstView.VisibleLines, SecondView.VisibleLines));
            CompareScrollBar.ViewportSize = Math.Min(FirstView.VisibleLines, SecondView.VisibleLines);
            CompareScrollBar.LargeChange = Math.Max(1, CompareScrollBar.ViewportSize - 1);
            CompareScrollBar.SmallChange = 1;
            CompareScrollBar.Value = Math.Min(CompareScrollBar.Maximum, FirstView.FirstLine);
        }
        finally
        {
            _updatingScrollBar = false;
        }
    }

    private void UpdateStatus()
    {
        StatusText.Text = $"Differences: {_result.DifferenceCount}";
        OffsetText.Text = $"Offset(h): {_currentOffset:X}    {_first.Label} size(h): {_first.Buffer.Length:X}    {_second.Label} size(h): {_second.Buffer.Length:X}";
        UpdateInspector(
            _first.Buffer,
            FirstSizeHexText,
            FirstOffsetHexText,
            FirstCharText,
            FirstBitText,
            FirstByteHexText,
            FirstWordHexText,
            FirstDwordHexText);
        UpdateInspector(
            _second.Buffer,
            SecondSizeHexText,
            SecondOffsetHexText,
            SecondCharText,
            SecondBitText,
            SecondByteHexText,
            SecondWordHexText,
            SecondDwordHexText);
    }

    private static string DisplayName(MemoryBufferOption memory) =>
        string.IsNullOrWhiteSpace(memory.SourceFileName) ? memory.Label : memory.SourceFileName;

    private void UpdateInspector(
        byte[] buffer,
        System.Windows.Controls.TextBlock sizeHex,
        System.Windows.Controls.TextBlock offsetHex,
        System.Windows.Controls.TextBlock charText,
        System.Windows.Controls.TextBlock bitText,
        System.Windows.Controls.TextBlock byteHex,
        System.Windows.Controls.TextBlock wordHex,
        System.Windows.Controls.TextBlock dwordHex)
    {
        sizeHex.Text = $"- HEX       0x{buffer.Length:X}";
        offsetHex.Text = $"- HEX       0x{_currentOffset:X}";
        if ((uint)_currentOffset >= buffer.Length)
        {
            charText.Text = "- Character --";
            bitText.Text = "- BitSet    --";
            byteHex.Text = "- Byte (HEX) --";
            wordHex.Text = "- Word (HEX) --";
            dwordHex.Text = "- DWord(HEX) --";
            return;
        }

        var value = buffer[_currentOffset];
        charText.Text = $"- Character {(value is >= 32 and <= 126 ? ((char)value).ToString() : ".")}";
        bitText.Text = $"- BitSet    {Convert.ToString(value, 2).PadLeft(8, '0')}";
        byteHex.Text = $"- Byte (HEX) 0x{value:X2}";
        SetNumber(wordHex, "Word", ReadLittleEndian(buffer, _currentOffset, 2));
        SetNumber(dwordHex, "DWord", ReadLittleEndian(buffer, _currentOffset, 4));
    }

    private static void SetNumber(System.Windows.Controls.TextBlock hexText, string label, uint? value)
    {
        hexText.Text = value is null ? $"- {label} (HEX) --" : $"- {label} (HEX) 0x{value:X}";
    }

    private static uint? ReadLittleEndian(byte[] buffer, int offset, int count)
    {
        if (offset < 0 || offset + count > buffer.Length)
        {
            return null;
        }

        uint value = 0;
        for (var i = 0; i < count; i++)
        {
            value |= (uint)buffer[offset + i] << (8 * i);
        }

        return value;
    }
}
