using Microsoft.Win32;
using System.Windows;

namespace NexusProgrammer;

public partial class ClearMeWindow : Window
{
    private readonly Func<MemoryBufferOption, string, string, Task> _clearSingleBios;

    public ClearMeWindow(IEnumerable<MemoryBufferOption> memoryTabs, Func<MemoryBufferOption, string, string, Task> clearSingleBios)
    {
        InitializeComponent();
        _clearSingleBios = clearSingleBios;
        MemoryCombo.ItemsSource = memoryTabs.ToList();
        MemoryCombo.DisplayMemberPath = nameof(MemoryBufferOption.Label);
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

    private async void ClearMe_Click(object sender, RoutedEventArgs e)
    {
        if (MemoryCombo.SelectedItem is not MemoryBufferOption memory)
        {
            MessageBox.Show(this, "Select a Memory tab first.", "Clear ME", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var meRegion = MeRegionCombo.Text.Trim();
        var fit = FitCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(meRegion) || string.IsNullOrWhiteSpace(fit))
        {
            MessageBox.Show(this, "Select ME Region and FIT first.", "Clear ME", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClearMeButton.IsEnabled = false;
        try
        {
            await _clearSingleBios(memory, meRegion, fit);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Clear ME", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ClearMeButton.IsEnabled = true;
        }
    }
}

public sealed record MemoryBufferOption(string Label, byte[] Buffer);
