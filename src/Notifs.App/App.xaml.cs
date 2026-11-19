using System.Globalization;
using System.IO;
using System.Windows;
using Notifs.Core;

namespace Notifs.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
        // Explicit override isolates manual/test runs from the real user profile.
        var dataDirectory = Environment.GetEnvironmentVariable("NOTIFS_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notifs");
        var service = new TaskService(Path.Combine(dataDirectory, "notifs.db"));
        var model = new MainViewModel(service);
        var window = new MainWindow(model);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var notifications = new NotificationRuntime(service, model, window);
        notifications.Start();
        window.Show();
    }
}
