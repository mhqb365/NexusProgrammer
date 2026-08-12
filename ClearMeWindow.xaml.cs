using Microsoft.Win32;
using System.Windows;

namespace NexusProgrammer;

public partial class ClearMeWindow : Window
{
    public ClearMeWindow(IEnumerable<string> memoryTabs)
    {
        InitializeComponent();
        MemoryCombo.ItemsSource = memoryTabs.ToList();
        MemoryCombo.SelectedIndex = MemoryCombo.Items.Count > 0 ? 0 : -1;
    }

    private void ClearMemory_Click(object sender, RoutedEventArgs e)
    {
        MemoryCombo.SelectedIndex = -1;
    }

    private void BrowseMeRegion_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(MeRegionCombo);
    }

    private void BrowseFit_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(FitCombo);
    }

    private void BrowseInto(System.Windows.Controls.ComboBox comboBox)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!comboBox.Items.Contains(dialog.FileName))
        {
            comboBox.Items.Add(dialog.FileName);
        }

        comboBox.SelectedItem = dialog.FileName;
    }
}
