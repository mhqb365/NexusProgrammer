using System.Windows;

namespace NexusProgrammer;

public partial class HexComparePickerWindow : Window
{
    public HexComparePickerWindow(IEnumerable<MemoryBufferOption> memoryTabs)
    {
        InitializeComponent();
        var options = memoryTabs.ToList();
        Bios1Combo.ItemsSource = options;
        Bios2Combo.ItemsSource = options;
        Bios1Combo.DisplayMemberPath = nameof(MemoryBufferOption.Label);
        Bios2Combo.DisplayMemberPath = nameof(MemoryBufferOption.Label);
        Bios1Combo.SelectedIndex = options.Count > 0 ? 0 : -1;
        Bios2Combo.SelectedIndex = options.Count > 1 ? 1 : -1;
        Bios1Combo.SelectionChanged += (_, _) => UpdateCompareButton();
        Bios2Combo.SelectionChanged += (_, _) => UpdateCompareButton();
        UpdateCompareButton();
    }

    public MemoryBufferOption? Bios1 => Bios1Combo.SelectedItem as MemoryBufferOption;
    public MemoryBufferOption? Bios2 => Bios2Combo.SelectedItem as MemoryBufferOption;

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (Bios1 is null || Bios2 is null || ReferenceEquals(Bios1, Bios2))
        {
            MessageBox.Show(this, "Select two different Memory tabs first.", "Hex Compare", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void UpdateCompareButton()
    {
        CompareButton.IsEnabled = Bios1 is not null && Bios2 is not null && !ReferenceEquals(Bios1, Bios2);
    }
}
