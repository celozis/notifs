using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Notifs.Core;

public sealed partial class TaskService
{
    public IReadOnlyList<SignalReminder> ListSignals()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM signal_reminders ORDER BY rowid";
        using var reader = command.ExecuteReader();
        var result = new List<SignalReminder>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<SignalReminder>(reader.GetString(0))!);
        return result;
    }

    private static SignalReminder ReadSignal(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT data FROM signal_reminders WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<SignalReminder>(json)! :
            throw new KeyNotFoundException("Напоминание больше не существует.");
    }

    private static void WriteSignal(SqliteConnection connection, SqliteTransaction transaction, SignalReminder item)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO signal_reminders(id,data) VALUES($id,$data) ON CONFLICT(id) DO UPDATE SET data=excluded.data";
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(item));
        command.ExecuteNonQuery();
    }

    public SignalReminder SaveSignal(Guid? id, string title, string description, SignalSchedule schedule, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Введите название напоминания.");
        schedule.Validate(time.LocalTimeZone);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var previous = id is { } key ? ReadSignal(connection, transaction, key) : null;
        var unchanged = previous is { Enabled: true } && previous.Schedule.SameRule(schedule);
        var next = !enabled ? null : unchanged ? previous!.NextAt : schedule.Next(time.GetUtcNow(), time.LocalTimeZone, previous is null);
        var item = new SignalReminder(id ?? Guid.NewGuid(), title.Trim(), description, schedule, enabled, next,
            enabled && unchanged ? previous!.SnoozedUntil : null, Guid.NewGuid());
        WriteSignal(connection, transaction, item);
        transaction.Commit();
        TaskNotificationsChanged?.Invoke(new("signal", item.Id), false);
        return item;
    }

    public SignalReminder SetSignalEnabled(Guid id, bool enabled)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var previous = ReadSignal(connection, transaction, id);
        var item = previous with { Enabled = enabled, Revision = Guid.NewGuid(), SnoozedUntil = null,
            NextAt = enabled ? previous.Schedule.Next(time.GetUtcNow(), time.LocalTimeZone) : null };
        WriteSignal(connection, transaction, item);
        transaction.Commit();
        TaskNotificationsChanged?.Invoke(new("signal", id), false);
        return item;
    }

    public void SnoozeSignal(Guid id)
    {
        var minutes = ReadNotificationSettings().SnoozeMinutes;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var item = ReadSignal(connection, transaction, id);
        if (!item.Enabled) throw new KeyNotFoundException("Напоминание выключено.");
        WriteSignal(connection, transaction, item with { SnoozedUntil = time.GetUtcNow().AddMinutes(minutes) });
        transaction.Commit();
        TaskNotificationsChanged?.Invoke(new("signal", id), false);
    }

    public IReadOnlyList<NotificationEvent> PollSignals()
    {
        var now = time.GetUtcNow();
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT data FROM signal_reminders ORDER BY rowid";
        var due = new List<SignalReminder>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var item = JsonSerializer.Deserialize<SignalReminder>(reader.GetString(0))!;
                if (item.Enabled && (item.SnoozedUntil ?? item.NextAt) <= now) due.Add(item);
            }
        var events = new List<NotificationEvent>();
        foreach (var item in due)
        {
            WriteSignal(connection, transaction, item with { NextAt = item.Schedule.Next(now, time.LocalTimeZone), SnoozedUntil = null });
            events.Add(new(Guid.NewGuid(), NotificationKind.Signal, item.Revision.ToString(),
                [new(new("signal", item.Id), item.Title, item.Description)], [new("snooze", "Отложить")]));
        }
        transaction.Commit();
        return events;
    }

    public bool IsSignalCurrent(NotificationEvent notification) => ListSignals().Any(item => item.Id == notification.Subjects[0].Key.Id &&
        item.Enabled && item.Revision.ToString() == notification.SourceId && item.SnoozedUntil is null);

    public void DeleteSignal(Guid id)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var item = ReadSignal(connection, transaction, id);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO deleted_item(slot,kind,data) VALUES(1,'signal',$data) ON CONFLICT(slot) DO UPDATE SET kind=excluded.kind,data=excluded.data;
            DELETE FROM signal_reminders WHERE id=$id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(item));
        command.ExecuteNonQuery();
        transaction.Commit();
        TaskNotificationsChanged?.Invoke(new("signal", id), false);
    }

    public string? DeletedKind
    {
        get
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT kind FROM deleted_item WHERE slot=1";
            return command.ExecuteScalar() as string;
        }
    }

    public EntityKey RestoreLastDeleted()
    {
        if (DeletedKind != "signal") return new("task", RestoreDeleted().Id);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT data FROM deleted_item WHERE slot=1 AND kind='signal'";
        if (command.ExecuteScalar() is not string json) throw new KeyNotFoundException("Нет удалённого напоминания.");
        var item = JsonSerializer.Deserialize<SignalReminder>(json)!;
        WriteSignal(connection, transaction, item with { Enabled = false, NextAt = null, SnoozedUntil = null, Revision = Guid.NewGuid() });
        command.CommandText = "DELETE FROM deleted_item WHERE slot=1";
        command.ExecuteNonQuery();
        transaction.Commit();
        return new("signal", item.Id);
    }
}
