using Microsoft.Win32;
using System.Windows;

namespace NexusProgrammer;

public partial class SettingsWindow : Window
{
    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        MeRegionRootBox.Text = settings.MeRegionRoot;
        FitRootBox.Text = settings.FitRoot;
        SoundEnabledCheckBox.IsChecked = settings.SoundEnabled;
    }

    public AppSettings Settings { get; private set; } = new();

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
        Settings = new AppSettings
        {
            MeRegionRoot = MeRegionRootBox.Text.Trim(),
            FitRoot = FitRootBox.Text.Trim(),
            SoundEnabled = SoundEnabledCheckBox.IsChecked == true
        };
        AppSettingsService.Save(Settings);
        DialogResult = true;
        Close();
    }
}
