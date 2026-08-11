using System.Windows;
using System.IO;
using System.Text;

namespace NexusProgrammer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog(args.Exception);
            MessageBox.Show(args.Exception.Message, "Nexus Programmer", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "NexusProgrammer.crash.log");
            var log = new StringBuilder()
                .AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                .AppendLine(ex.ToString())
                .AppendLine()
                .ToString();
            File.AppendAllText(path, log);
        }
        catch
        {
        }
    }
}
