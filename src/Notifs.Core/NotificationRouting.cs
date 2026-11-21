using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Notifs.Core;

public sealed record MonitorSettings(bool Automatic = true, string? FixedMonitorId = null, string? FullscreenMonitorId = null);
public sealed record PixelArea(int Left, int Top, int Right, int Bottom);
public sealed record NotificationMonitor(string Id, string Name, bool IsPrimary, PixelArea Bounds, PixelArea WorkArea);
public static class NotificationRouting
{
    public static NotificationMonitor? Select(MonitorSettings settings, IReadOnlyList<NotificationMonitor> monitors, bool fullscreenOnPrimary)
    {
        var primary = monitors.FirstOrDefault(x => x.IsPrimary) ?? monitors.FirstOrDefault();
        if (!settings.Automatic)
            return monitors.FirstOrDefault(x => x.Id == settings.FixedMonitorId) ?? (fullscreenOnPrimary ? null : primary);
        if (!fullscreenOnPrimary) return primary;
        return settings.FullscreenMonitorId == null ? monitors.FirstOrDefault(x => !x.IsPrimary) :
            monitors.FirstOrDefault(x => !x.IsPrimary && x.Id == settings.FullscreenMonitorId);
    }
}

public sealed partial class TaskService
{
    public MonitorSettings ReadMonitorSettings()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM monitor_settings WHERE slot=1";
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<MonitorSettings>(json)! : new();
    }
    private static void WriteMonitorSettings(SqliteConnection connection, SqliteTransaction transaction, MonitorSettings settings)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO monitor_settings(slot,data) VALUES(1,$data) ON CONFLICT(slot) DO UPDATE SET data=excluded.data";
        command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(settings)); command.ExecuteNonQuery();
    }
}
