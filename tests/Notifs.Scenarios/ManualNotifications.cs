using System.Windows;
using Notifs.App;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static class ManualNotifications
{
    [STAThread]
    public static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Notifs.App;component/Theme.xaml", UriKind.Relative) });
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Notifs.Manual", Guid.NewGuid().ToString("N"), "notifs.db");
        var service = new TaskService(path);
        service.Initialize(); service.SaveNotificationSettings(new(false, 30));
        var first = DateTime.Now.AddSeconds(40);
        foreach (var title in new[] { "Закончить практическую работу", "Отправить домашнюю работу" })
            service.SaveEditor(null, title, "Проверка настоящей доставки без перехвата фокуса.", null, 40, ImportanceRank.A,
                new(DateOnly.FromDateTime(first), TimeOnly.FromDateTime(first), 1));
        var model = new MainViewModel(service);
        var main = new MainWindow(model) { Title = "Notifs QA — личные напоминания" };
        app.MainWindow = main;
        using var output = new NativeNotificationOutput();
        using var runtime = new NotificationRuntime(service, model, main, output);
        runtime.Start();
        var inspection = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        inspection.Tick += (_, _) =>
        {
            Console.WriteLine($"NATIVE visible={output.VisibleEvents.Count}; foreground={GetForegroundWindow()}; next={service.ReadPersonal(service.List()[0].Id)?.NextAt:O}; now={DateTimeOffset.Now:O}");
            if (output.Window.IsVisible) UiScenario.SaveImage(output.Window, "manual-notifications.png");
        };
        inspection.Start();
        try { return app.Run(main); }
        finally
        {
            inspection.Stop();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(path)!, true);
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
