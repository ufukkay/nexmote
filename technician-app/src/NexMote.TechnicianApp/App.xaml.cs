using System.Windows;

namespace NexMote.TechnicianApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        if (!window.CredentialsReady)
        {
            Shutdown();
            return;
        }

        MainWindow = window;
        window.Show();
    }
}

