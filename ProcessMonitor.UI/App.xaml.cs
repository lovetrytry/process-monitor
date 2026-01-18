using System.Configuration;
using System.Data;
using System.IO;
using System.Threading;
using System.Windows;

namespace ProcessMonitor.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static Mutex _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string appName = "ProcessMonitor.UI.SingleInstance";
        bool createdNew;

        _mutex = new Mutex(true, appName, out createdNew);

        if (!createdNew)
        {
            MessageBox.Show("Process Monitor is already running.", "Instance Error", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

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

