using System.Windows;

namespace NexusProgrammer;

public partial class BiosMemoryPickerWindow : Window
{
    private readonly int _minimumSelectionCount;
    private readonly int _maximumSelectionCount;

    public BiosMemoryPickerWindow(
        string title,
        string prompt,
        IEnumerable<MemoryBufferOption> memoryTabs,
        int minimumSelectionCount,
        int maximumSelectionCount)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        _minimumSelectionCount = minimumSelectionCount;
        _maximumSelectionCount = maximumSelectionCount;

        var options = memoryTabs.ToList();
        MemoryList.ItemsSource = options;
        MemoryList.SelectionMode = maximumSelectionCount == 1
            ? System.Windows.Controls.SelectionMode.Single
            : System.Windows.Controls.SelectionMode.Extended;

        if (options.Count > 0)
        {
            MemoryList.SelectedIndex = 0;
        }

        if (maximumSelectionCount > 1 && options.Count > 1)
        {
            MemoryList.SelectedItems.Add(options[1]);
        }
    }

    public IReadOnlyList<MemoryBufferOption> SelectedMemories =>
        MemoryList.SelectedItems.Cast<MemoryBufferOption>().ToList();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var count = MemoryList.SelectedItems.Count;
        if (count < _minimumSelectionCount || count > _maximumSelectionCount)
        {
            var message = _maximumSelectionCount == _minimumSelectionCount
                ? $"Select {_minimumSelectionCount} Memory tab(s)."
                : $"Select from {_minimumSelectionCount} to {_maximumSelectionCount} Memory tabs.";
            MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
