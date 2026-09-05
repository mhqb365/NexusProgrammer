using System.Diagnostics;
using System.Windows;

namespace NexusProgrammer;

public partial class AboutWindow : Window
{
    private const string SourceCodeUrl = "https://github.com/mhqb365/NexusProgrammer";
    private const string AuthorUrl = "https://mhqb365.com";

    public AboutWindow()
    {
        InitializeComponent();
        TitleText.Text = $"Nexus Programmer v{UpdateService.FormatVersion(UpdateService.CurrentVersion)}";
    }

    private void SourceCode_Click(object sender, RoutedEventArgs e) => OpenUrl(SourceCodeUrl);

    private void Author_Click(object sender, RoutedEventArgs e) => OpenUrl(AuthorUrl);

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }
}
