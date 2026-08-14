using Microsoft.Win32;
using System.Windows;

namespace NexusProgrammer;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        var settings = AppSettingsService.Load();
        MeRegionRootBox.Text = settings.MeRegionRoot;
        FitRootBox.Text = settings.FitRoot;
    }

    private void BrowseMeRegionRoot_Click(object sender, RoutedEventArgs e)
    {
        BrowseFolderInto(MeRegionRootBox);
    }

    private void BrowseFitRoot_Click(object sender, RoutedEventArgs e)
    {
        BrowseFolderInto(FitRootBox);
    }

    private void BrowseFolderInto(System.Windows.Controls.TextBox textBox)
    {
        var dialog = new OpenFolderDialog
        {
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(textBox.Text))
        {
            dialog.InitialDirectory = textBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            textBox.Text = dialog.FolderName;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsService.Save(new AppSettings
        {
            MeRegionRoot = MeRegionRootBox.Text.Trim(),
            FitRoot = FitRootBox.Text.Trim()
        });
        DialogResult = true;
        Close();
    }
}
