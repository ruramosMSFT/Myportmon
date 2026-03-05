using System.Windows;
using System.Windows.Threading;

namespace PortMonitor.Gui;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Catch any unhandled exception from WPF dispatcher or background threads
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(
                $"Unhandled error:\n\n{ex.Exception}",
                "PortMonitor — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ex.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            MessageBox.Show(
                $"Fatal error:\n\n{ex.ExceptionObject}",
                "PortMonitor — Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        base.OnStartup(e);
    }
}
