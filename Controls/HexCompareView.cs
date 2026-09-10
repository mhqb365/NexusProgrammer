using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NexusProgrammer;

public sealed class HexCompareView : FrameworkElement
{
    private const int BytesPerLine = 16;
    private const double LineHeight = 18;
    private const double AddressX = 10;
    private const double HexX = 96;
    private const double PreferredAsciiX = 506;
    private const double ByteCellWidth = 24;
    private const double CharCellWidth = 8;
    private static readonly Brush BackgroundBrush = FrozenBrush(Color.FromRgb(252, 253, 255));
    private static readonly Brush DiffBrush = FrozenBrush(Color.FromRgb(255, 218, 218));
    private static readonly Brush MirrorSelectionBrush = FrozenBrush(Color.FromRgb(235, 250, 253));
    private static readonly Brush SelectionBrush = FrozenBrush(Color.FromRgb(220, 246, 252));
    private static readonly Brush CurrentBrush = FrozenBrush(Color.FromRgb(190, 232, 248));
    private static readonly Pen CurrentPen = FrozenPen(Brushes.DeepSkyBlue, 1);
    private static readonly Typeface TextTypeface = new("Consolas");

    private byte[] _buffer = [];
    private HashSet<int> _diffOffsets = [];
    private int _firstLine;
    private int _currentOffset;
    private int _selectionAnchor;
    private int _selectionEnd;
    private int? _mirrorSelectionStart;
    private int _mirrorSelectionLength;
    private bool _isSelecting;

    public HexCompareView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public int FirstLine => _firstLine;
    public int SelectionStart => Math.Min(_selectionAnchor, _selectionEnd);
    public int SelectionLength => _buffer.Length == 0
        ? 0
        : Math.Max(0, Math.Min(Math.Max(_selectionAnchor, _selectionEnd), _buffer.Length - 1) - SelectionStart + 1);
    public int TotalLines => Math.Max(1, (_buffer.Length + BytesPerLine - 1) / BytesPerLine);
    public int VisibleLines => Math.Max(1, (int)(ActualHeight / LineHeight));

    public event EventHandler? ScrollChanged;
    public event EventHandler<int>? OffsetClicked;
    public event EventHandler? SelectionChanged;

    public void SetData(byte[] buffer, IEnumerable<int> diffOffsets)
    {
        _buffer = buffer;
        _diffOffsets = diffOffsets.Where(offset => (uint)offset < buffer.Length).ToHashSet();
        _firstLine = 0;
        _currentOffset = 0;
        _selectionAnchor = 0;
        _selectionEnd = 0;
        ClearMirrorSelection();
        InvalidateVisual();
    }

    public void ScrollToOffset(int offset)
    {
        if (_buffer.Length == 0)
        {
            return;
        }

        _currentOffset = Math.Clamp(offset, 0, _buffer.Length - 1);
        var line = _currentOffset / BytesPerLine;
        SetFirstLine(Math.Max(0, line - VisibleLines / 2));
    }

    public void SetCurrentOffset(int offset)
    {
        if (_buffer.Length == 0)
        {
            return;
        }

        _currentOffset = Math.Clamp(offset, 0, _buffer.Length - 1);
        InvalidateVisual();
    }

    public void SetFirstLine(int line)
    {
        var max = Math.Max(0, TotalLines - VisibleLines);
        var next = Math.Clamp(line, 0, max);
        if (_firstLine == next)
        {
            InvalidateVisual();
            return;
        }

        _firstLine = next;
        ScrollChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void SetMirrorSelection(int start, int length)
    {
        if (_buffer.Length == 0 || length <= 1)
        {
            ClearMirrorSelection();
            return;
        }

        _mirrorSelectionStart = Math.Clamp(start, 0, _buffer.Length - 1);
        _mirrorSelectionLength = Math.Min(length, _buffer.Length - _mirrorSelectionStart.Value);
        InvalidateVisual();
    }

    public void ClearMirrorSelection()
    {
        _mirrorSelectionStart = null;
        _mirrorSelectionLength = 0;
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        _selectionAnchor = _currentOffset;
        _selectionEnd = _currentOffset;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var addressBrush = Brushes.Blue;
        var textBrush = Brushes.Black;
        var asciiX = Math.Min(PreferredAsciiX, Math.Max(HexX + BytesPerLine * ByteCellWidth + 24, ActualWidth - 150));
        var selectionStart = Math.Min(_selectionAnchor, _selectionEnd);
        var selectionEnd = Math.Max(_selectionAnchor, _selectionEnd);

        for (var row = 0; row < VisibleLines; row++)
        {
            var offset = (_firstLine + row) * BytesPerLine;
            if (offset >= _buffer.Length)
            {
                break;
            }

            var y = row * LineHeight;
            DrawText(dc, $"{offset:X8}", AddressX, y, addressBrush);
            for (var i = 0; i < BytesPerLine && offset + i < _buffer.Length; i++)
            {
                var byteOffset = offset + i;
                var x = HexX + i * ByteCellWidth;
                if (_diffOffsets.Contains(byteOffset))
                {
                    dc.DrawRectangle(DiffBrush, null, new Rect(x - 2, y, 20, LineHeight));
                    dc.DrawRectangle(DiffBrush, null, new Rect(asciiX + i * CharCellWidth - 1, y, CharCellWidth, LineHeight));
                }
                if (IsMirrorSelected(byteOffset))
                {
                    dc.DrawRectangle(MirrorSelectionBrush, null, new Rect(x - 2, y, 20, LineHeight));
                    dc.DrawRectangle(MirrorSelectionBrush, null, new Rect(asciiX + i * CharCellWidth - 1, y, CharCellWidth, LineHeight));
                }
                if (byteOffset >= selectionStart && byteOffset <= selectionEnd)
                {
                    dc.DrawRectangle(SelectionBrush, null, new Rect(x - 2, y, 20, LineHeight));
                    dc.DrawRectangle(SelectionBrush, null, new Rect(asciiX + i * CharCellWidth - 1, y, CharCellWidth, LineHeight));
                }
                if (byteOffset == _currentOffset)
                {
                    dc.DrawRectangle(CurrentBrush, null, new Rect(x - 2, y, 20, LineHeight));
                    dc.DrawRectangle(CurrentBrush, null, new Rect(asciiX + i * CharCellWidth - 1, y, CharCellWidth, LineHeight));
                    dc.DrawRectangle(null, CurrentPen, new Rect(x - 2, y, 20, LineHeight));
                    dc.DrawRectangle(null, CurrentPen, new Rect(asciiX + i * CharCellWidth - 1, y, CharCellWidth, LineHeight));
                }

                DrawText(dc, _buffer[byteOffset].ToString("X2", CultureInfo.InvariantCulture), x, y, textBrush);
                var b = _buffer[byteOffset];
                DrawText(dc, b is >= 32 and <= 126 ? ((char)b).ToString() : ".", asciiX + i * CharCellWidth, y, textBrush);
            }
        }
    }

    protected override void OnMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
    {
        SetFirstLine(_firstLine - e.Delta / 120 * 3);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (TryHitTestOffset(e.GetPosition(this), out var offset))
        {
            _mirrorSelectionStart = null;
            _mirrorSelectionLength = 0;
            _currentOffset = offset;
            _selectionAnchor = offset;
            _selectionEnd = offset;
            _isSelecting = true;
            CaptureMouse();
            OffsetClicked?.Invoke(this, offset);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        if (TryHitTestOffset(e.GetPosition(this), out var offset))
        {
            _currentOffset = offset;
            _selectionEnd = offset;
            OffsetClicked?.Invoke(this, offset);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        _isSelecting = false;
        ReleaseMouseCapture();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.C)
        {
            CopySelection();
            e.Handled = true;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        SetFirstLine(_firstLine);
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void DrawText(DrawingContext dc, string text, double x, double y, Brush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            TextTypeface, 12, brush, 1.0);
        dc.DrawText(formatted, new Point(x, y));
    }

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private bool TryHitTestOffset(Point p, out int offset)
    {
        offset = 0;
        var row = (int)(p.Y / LineHeight);
        if (row < 0)
        {
            return false;
        }

        var asciiX = Math.Min(PreferredAsciiX, Math.Max(HexX + BytesPerLine * ByteCellWidth + 24, ActualWidth - 150));
        var lineOffset = (_firstLine + row) * BytesPerLine;
        var byteIndex = p.X >= asciiX
            ? (int)((p.X - asciiX) / CharCellWidth)
            : (int)((p.X - HexX) / ByteCellWidth);
        if (byteIndex < 0 || byteIndex >= BytesPerLine)
        {
            return false;
        }

        offset = lineOffset + byteIndex;
        return (uint)offset < _buffer.Length;
    }

    private bool IsMirrorSelected(int offset) =>
        _mirrorSelectionStart is int start && offset >= start && offset < start + _mirrorSelectionLength;

    private void CopySelection()
    {
        if (SelectionLength <= 0)
        {
            return;
        }

        var start = SelectionStart;
        var end = Math.Min(start + SelectionLength, _buffer.Length);
        Clipboard.SetText(string.Join(" ", _buffer[start..end].Select(b => b.ToString("X2", CultureInfo.InvariantCulture))));
    }
}
