using System.Windows;

namespace SensiboWidget;

public partial class App : Application
{
    private Mutex? instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var demo = e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
        instance = new Mutex(true, demo ? @"Local\SensiboDesktopWidget.Demo" : @"Local\SensiboDesktopWidget", out var created);
        if (!created)
        {
            MessageBox.Show("The widget is already running. Use its tray icon to show it or open Settings.", "Sensibo widget");
            Shutdown();
            return;
        }
        try
        {
            var window = new MainWindow(new StateStore(), demo);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            MessageBox.Show("The widget could not open its local settings. Check access to the SensiboDesktopWidget folder in Local AppData.", "Sensibo widget");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
