using System.Globalization;
using System.IO;
using System.Windows;
using Notifs.Core;

namespace Notifs.App;

public partial class App : Application
{
    private SingleInstance? instance;
    private DesktopLifetime? lifetime;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
        // Explicit override isolates manual/test runs from the real user profile.
        var dataDirectory = Environment.GetEnvironmentVariable("NOTIFS_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notifs");
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        instance = new SingleInstance(dataDirectory);
        if (!instance.IsOwner) { instance.ActivateOwner(); Shutdown(); return; }
        var service = new TaskService(Path.Combine(dataDirectory, "notifs.db"));
        var model = new MainViewModel(service);
        var window = new MainWindow(model);
        MainWindow = window;
        var notifications = new NotificationRuntime(service, model, window);
        lifetime = new DesktopLifetime(window, notifications, () => Shutdown());
        instance.Listen(() => Dispatcher.BeginInvoke(() => lifetime?.Show()));
        if (e.Args.Contains("--background")) await model.LoadAsync();
        else window.Show();
        notifications.Start();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        lifetime?.Dispose(); instance?.Dispose();
        base.OnExit(e);
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (lifetime?.TryExit() == false) e.Cancel = true;
        base.OnSessionEnding(e);
    }
}
