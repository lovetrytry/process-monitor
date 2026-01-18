using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;

namespace ProcessMonitor.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            LogException((Exception)args.ExceptionObject, "AppDomain.UnhandledException");

        DispatcherUnhandledException += (s, args) =>
        {
            LogException(args.Exception, "DispatcherUnhandledException");
            args.Handled = true;
            MessageBox.Show($"Application Crash: {args.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        };

        base.OnStartup(e);
    }

    private void LogException(Exception ex, string source)
    {
        try
        {
            string logFile = "crash.log";
            string message = $"[{DateTime.Now}] [{source}] {ex.Message}\nStack Trace:\n{ex.StackTrace}\n\n";
            System.IO.File.AppendAllText(logFile, message);
        }
        catch { }
    }
}

