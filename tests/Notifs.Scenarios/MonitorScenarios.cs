using Notifs.Core;

namespace Notifs.Scenarios;

internal static class MonitorScenarios
{
    public static void Run(string directory)
    {
        var primary = new NotificationMonitor("primary", "Основной", true, new(0, 0, 1920, 1080), new(0, 0, 1920, 1040));
        var secondary = new NotificationMonitor("second", "Дополнительный", false, new(-1280, 0, 0, 1024), new(-1280, 0, 0, 984));
        var settings = new MonitorSettings(true, primary.Id, secondary.Id);
        DraftScenarios.Check(NotificationRouting.Select(settings, [primary, secondary], false) == primary && NotificationRouting.Select(settings, [primary, secondary], true) == secondary, "automatic route uses primary normally and selected secondary in foreground fullscreen");
        DraftScenarios.Check(NotificationRouting.Select(settings, [primary], true) == null && NotificationRouting.Select(settings, [primary], false) == primary, "missing selected secondary never falls back over primary fullscreen");
        DraftScenarios.Check(NotificationRouting.Select(settings with { Automatic = false, FixedMonitorId = secondary.Id }, [primary, secondary], false) == secondary, "fixed route honors selected physical monitor independently of fullscreen");
        var service = new TaskService(System.IO.Path.Combine(directory, "monitors.db")); service.Initialize();
        service.SaveSettings(new(false, 15), service.ReadSummarySettings(), settings);
        DraftScenarios.Check(new TaskService(System.IO.Path.Combine(directory, "monitors.db")).ReadMonitorSettings() == settings, "stable monitor identities persist independently of other settings");
    }
}
