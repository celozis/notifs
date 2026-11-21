using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Notifs.Core;

public sealed record SummarySettings(bool Enabled, int IntervalMinutes, int TaskCount, DateOnly FirstDate, TimeOnly FirstTime);

public sealed partial class TaskService
{
    private sealed record SummaryState(SummarySettings Settings, DateTimeOffset FirstAt, DateTimeOffset? NextAt);

    private void InitializeSummary(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "CREATE TABLE IF NOT EXISTS summary_settings (slot INTEGER PRIMARY KEY CHECK(slot=1), data TEXT NOT NULL)";
        command.ExecuteNonQuery();
        if (ReadSummary(connection, transaction) is not null) return;
        var local = TimeZoneInfo.ConvertTime(time.GetUtcNow().AddMinutes(180), time.LocalTimeZone);
        var settings = new SummarySettings(true, 180, 3, DateOnly.FromDateTime(local.DateTime), new TimeOnly(local.Hour, local.Minute));
        var first = new TaskDeadline(settings.FirstDate, settings.FirstTime).Resolve(time.LocalTimeZone);
        WriteSummary(connection, transaction, new(settings, first, first));
    }

    private static SummaryState? ReadSummary(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT data FROM summary_settings WHERE slot=1";
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<SummaryState>(json) : null;
    }

    private static void WriteSummary(SqliteConnection connection, SqliteTransaction transaction, SummaryState state)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO summary_settings(slot,data) VALUES(1,$data) ON CONFLICT(slot) DO UPDATE SET data=excluded.data";
        command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(state));
        command.ExecuteNonQuery();
    }

    public SummarySettings ReadSummarySettings()
    {
        using var connection = Open();
        return ReadSummary(connection, null)!.Settings;
    }

    /// <summary>Saves the two owned settings sections atomically, leaving other sections untouched.</summary>
    public void SaveSettings(NotificationSettings notifications, SummarySettings summary, MonitorSettings? monitors = null)
    {
        ValidateNotificationSettings(notifications);
        if (summary.IntervalMinutes < 1) throw new ArgumentException("Интервал сводки: положительное целое число минут.");
        if (summary.TaskCount is < 1 or > 10) throw new ArgumentException("Задач в карточке: от 1 до 10.");
        var first = new TaskDeadline(summary.FirstDate, summary.FirstTime).Resolve(time.LocalTimeZone);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var previous = ReadSummary(connection, transaction)!;
        var sameSchedule = previous.Settings.FirstDate == summary.FirstDate && previous.Settings.FirstTime == summary.FirstTime &&
            previous.Settings.IntervalMinutes == summary.IntervalMinutes;
        var next = sameSchedule ? previous.NextAt : SummaryOccurrence(first, summary.IntervalMinutes, time.GetUtcNow(), inclusive: true);
        if (sameSchedule && summary.Enabled && !previous.Settings.Enabled && next is { } cursor)
        {
            var current = SummaryOccurrence(first, summary.IntervalMinutes, time.GetUtcNow(), inclusive: true);
            next = current is null ? null : cursor > current ? cursor : current;
        }
        WriteNotificationSettings(connection, transaction, notifications);
        WriteSummary(connection, transaction, new(summary, first, next));
        if (monitors != null) WriteMonitorSettings(connection, transaction, monitors);
        transaction.Commit();
    }

    private static DateTimeOffset? SummaryOccurrence(DateTimeOffset first, int minutes, DateTimeOffset now, bool inclusive = false)
    {
        if (first > now || inclusive && first == now) return first;
        var interval = TimeSpan.FromMinutes(minutes).Ticks;
        var elapsed = (now - first).Ticks;
        var steps = elapsed / interval + (inclusive && elapsed % interval == 0 ? 0 : 1);
        var available = DateTimeOffset.MaxValue.UtcTicks - first.UtcTicks;
        return steps <= available / interval ? new DateTimeOffset(first.UtcTicks + steps * interval, TimeSpan.Zero) : null;
    }

    public IReadOnlyList<NotificationEvent> PollSummary()
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        var result = PollSummary(connection, transaction, time.GetUtcNow());
        transaction.Commit(); return result;
    }
    private IReadOnlyList<NotificationEvent> PollSummary(SqliteConnection connection, SqliteTransaction transaction, DateTimeOffset now)
    {
        var state = ReadSummary(connection, transaction)!;
        if (!state.Settings.Enabled || state.NextAt is not { } due || due > now) return [];
        WriteSummary(connection, transaction, state with { NextAt = SummaryOccurrence(state.FirstAt, state.Settings.IntervalMinutes, now) });
        // Only the current minute is deliverable. Older ticks are consumed, never replayed after downtime (#20).
        var subjects = now - due < TimeSpan.FromMinutes(1) ? ReadSummaryTasks(connection, transaction).Take(state.Settings.TaskCount).Select(SummarySubject).ToArray() : [];
        return subjects.Length == 0 ? [] : [new(Guid.NewGuid(), NotificationKind.Summary, "summary", subjects)];
    }

    private IReadOnlyList<TaskItem> ReadSummaryTasks(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {Columns} FROM tasks WHERE completed_at IS NULL";
        var tasks = new List<TaskItem>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) tasks.Add(ReadItem(reader));
        var now = time.GetUtcNow();
        return Order(tasks.Where(item => ReadPersonal(connection, transaction, item.Id)?.SnoozedUntil is not { } until || until <= now));
    }

    private NotificationSubject SummarySubject(TaskItem task)
    {
        var timing = Timing(task);
        var days = Math.Max(0, (timing.LocalNow.Date - timing.LocalCreatedAt.Date).Days);
        var age = days switch { 0 => "Создана сегодня", 1 => "Создана вчера", _ => $"Создана {days} дн. назад" };
        return new(new("task", task.Id), task.Title, age, task.Rank);
    }

    /// <summary>Revalidates a polled batch after asynchronous completion/deletion/snooze, without replacing its other rows.</summary>
    public NotificationEvent RefreshSummary(NotificationEvent notification)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var eligible = ReadSummaryTasks(connection, transaction).ToDictionary(x => x.Id);
        var subjects = ReadSummary(connection, transaction)!.Settings.Enabled
            ? notification.Subjects.Where(x => eligible.ContainsKey(x.Key.Id)).Select(x => SummarySubject(eligible[x.Key.Id])).ToArray() : [];
        return notification with { Subjects = subjects };
    }
}
