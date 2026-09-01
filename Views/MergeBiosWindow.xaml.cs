using System.Windows;

namespace NexusProgrammer;

public partial class MergeBiosWindow : Window
{
    public MergeBiosWindow(IEnumerable<MemoryBufferOption> memoryTabs)
    {
        InitializeComponent();
        var options = memoryTabs.ToList();
        Bios1Combo.ItemsSource = options;
        Bios1Combo.DisplayMemberPath = nameof(MemoryBufferOption.Label);
        Bios1Combo.SelectedIndex = options.Count > 0 ? 0 : -1;
        Bios2Combo.ItemsSource = options;
        Bios2Combo.DisplayMemberPath = nameof(MemoryBufferOption.Label);
        Bios2Combo.SelectedIndex = options.Count > 1 ? 1 : -1;
        Bios1Combo.SelectionChanged += (_, _) => UpdateMergeButton();
        Bios2Combo.SelectionChanged += (_, _) => UpdateMergeButton();
        UpdateMergeButton();
    }

    public MemoryBufferOption? Bios1 => Bios1Combo.SelectedItem as MemoryBufferOption;

    public MemoryBufferOption? Bios2 => Bios2Combo.SelectedItem as MemoryBufferOption;

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Bios1Combo.SelectedIndex = -1;
        Bios2Combo.SelectedIndex = -1;
        UpdateMergeButton();
    }

    private void Merge_Click(object sender, RoutedEventArgs e)
    {
        if (Bios1 is null || Bios2 is null || ReferenceEquals(Bios1, Bios2))
        {
            MessageBox.Show(this, "Select two different Memory tabs first.", "Merge BIOS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void UpdateMergeButton()
    {
        MergeButton.IsEnabled = Bios1 is not null && Bios2 is not null && !ReferenceEquals(Bios1, Bios2);
    }
}
