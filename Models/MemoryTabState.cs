using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace NexusProgrammer;

internal sealed class MemoryTabState
{
    public MemoryTabState(int index, TabItem tab, HexEditorView editor, ScrollBar scrollBar, byte[] buffer)
    {
        Index = index;
        Tab = tab;
        Editor = editor;
        ScrollBar = scrollBar;
        Buffer = buffer;
        DisplayName = $"Memory {index}";
    }

    public int Index { get; set; }
    public string DisplayName { get; set; }
    public TabItem Tab { get; }
    public HexEditorView Editor { get; }
    public ScrollBar ScrollBar { get; }
    public byte[] Buffer { get; set; }
    public MeaAnalysisResult? MeaAnalysis { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
}
