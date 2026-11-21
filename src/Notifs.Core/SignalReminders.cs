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

    public SignalReminder SaveSignal(Guid? id, string title, string description, SignalSchedule schedule, bool enabled,
        bool requiresCompletion = false, int repeatMinutes = 60, bool confirmClearDebt = false)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Введите название напоминания.");
        schedule.Validate(time.LocalTimeZone);
        if (!requiresCompletion && schedule.Repeat == SignalRepeat.AfterCompletion) throw new ArgumentException("Простой сигнал не требует выполнения.");
        if (requiresCompletion && repeatMinutes is < 1 or > 525600) throw new ArgumentException("Повтор до выполнения: от 1 до 525600 минут.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var previous = id is { } key ? ReadSignal(connection, transaction, key) : null;
        if (!requiresCompletion && previous?.Current is not null && !confirmClearDebt) throw new DebtConfirmationRequiredException();
        var unchanged = previous is { Enabled: true } && previous.RequiresCompletion == requiresCompletion && previous.Schedule.SameRule(schedule);
        var next = NextSavedReminder(previous, schedule, requiresCompletion, enabled);
        var item = new SignalReminder(id ?? Guid.NewGuid(), title.Trim(), description, schedule, enabled, next,
            requiresCompletion && previous?.Current is not null ? previous.SnoozedUntil : enabled && unchanged ? previous!.SnoozedUntil : null, Guid.NewGuid(),
            requiresCompletion, repeatMinutes, requiresCompletion ? previous?.Current : null, previous?.Completions);
        if (requiresCompletion && enabled) item = MaterializeDebt(item, time.GetUtcNow());
        WriteSignal(connection, transaction, item);
        transaction.Commit();
        TaskNotificationsChanged?.Invoke(new("signal", item.Id), false);
        if (previous?.Current is { } oldDebt) TaskNotificationsChanged?.Invoke(new("recurring", oldDebt.Id), false);
        return item;
    }

    private DateTimeOffset? NextSavedReminder(SignalReminder? previous, SignalSchedule schedule, bool requiresCompletion, bool enabled)
    {
        if (!requiresCompletion)
        {
            if (!enabled) return null;
            if (previous is { Enabled: true, RequiresCompletion: false } && previous.Schedule.SameRule(schedule)) return previous.NextAt;
            return schedule.Next(time.GetUtcNow(), time.LocalTimeZone, previous is null);
        }
        if (previous is { RequiresCompletion: true })
        {
            if (previous.Schedule.SameRule(schedule)) return previous.NextAt;
            // A changed rule starts the next cycle after the last actual completion, never at a consumed first date.
            if (previous.History.LastOrDefault() is { } last)
                return schedule.Repeat == SignalRepeat.AfterCompletion ? last.CompletedAt.Add(schedule.CompletionInterval)
                    : schedule.Next(last.CompletedAt, time.LocalTimeZone);
        }
        var first = new TaskDeadline(schedule.FirstDate, schedule.FirstTime).Resolve(time.LocalTimeZone);
        return schedule.Next(first, time.LocalTimeZone, true);
    }

    public SignalReminder SetSignalEnabled(Guid id, bool enabled)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var previous = ReadSignal(connection, transaction, id);
        var item = previous with { Enabled = enabled, Revision = Guid.NewGuid(),
            SnoozedUntil = previous.RequiresCompletion ? previous.SnoozedUntil : null,
            NextAt = previous.RequiresCompletion ? previous.NextAt : enabled ? previous.Schedule.Next(time.GetUtcNow(), time.LocalTimeZone) : null };
        if (item.RequiresCompletion && enabled)
        {
            item = MaterializeDebt(item, time.GetUtcNow());
            if (item.Current is { } debt && item.SnoozedUntil is null) item = item with { Current = debt with { NotifyAt = time.GetUtcNow() } };
        }
        WriteSignal(connection, transaction, item);
        transaction.Commit();
        TaskNotificationsChanged?.Invoke(new("signal", id), false);
        if (item.Current is { } current) TaskNotificationsChanged?.Invoke(new("recurring", current.Id), false);
        return item;
    }

    public void SnoozeSignal(Guid id)
    {
        var minutes = ReadNotificationSettings().SnoozeMinutes;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var item = ReadSignal(connection, transaction, id);
        if (item.RequiresCompletion) throw new ArgumentException("Откладывайте текущее дело по его идентификатору.");
        if (!item.Enabled) throw new KeyNotFoundException("Напоминание выключено.");
        WriteSignal(connection, transaction, item with { SnoozedUntil = time.GetUtcNow().AddMinutes(minutes) });
        transaction.Commit();
        TaskNotificationsChanged?.Invoke(new("signal", id), false);
    }

    public IReadOnlyList<NotificationEvent> PollSignals()
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        var result = PollSignals(connection, transaction, time.GetUtcNow());
        transaction.Commit(); return result;
    }
    private IReadOnlyList<NotificationEvent> PollSignals(SqliteConnection connection, SqliteTransaction transaction, DateTimeOffset now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT data FROM signal_reminders ORDER BY rowid";
        var due = new List<SignalReminder>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var item = JsonSerializer.Deserialize<SignalReminder>(reader.GetString(0))!;
                if (item.Enabled && (item.SnoozedUntil ?? item.Current?.NotifyAt ?? item.NextAt) <= now) due.Add(item);
            }
        var events = new List<NotificationEvent>();
        foreach (var item in due)
        {
            if (item.RequiresCompletion)
            {
                var updated = MaterializeDebt(item, now);
                var debt = updated.Current!;
                WriteSignal(connection, transaction, updated with { Current = debt with { NotifyAt = now.AddMinutes(item.RepeatMinutes) }, SnoozedUntil = null });
                events.Add(new(Guid.NewGuid(), NotificationKind.Recurring, item.Revision.ToString(),
                    [new(new("recurring", debt.Id), item.Title, $"Исходный срок: {TimeZoneInfo.ConvertTime(debt.DueAt, time.LocalTimeZone):dd.MM.yyyy HH:mm} · {item.Description}")],
                    [new("complete", "Готово"), new("snooze", "Отложить")]));
                continue;
            }
            WriteSignal(connection, transaction, item with { NextAt = item.Schedule.Next(now, time.LocalTimeZone), SnoozedUntil = null });
            events.Add(new(Guid.NewGuid(), NotificationKind.Signal, item.Revision.ToString(),
                [new(new("signal", item.Id), item.Title, item.Description)], [new("snooze", "Отложить")]));
        }
        return events;
    }

    public bool IsSignalCurrent(NotificationEvent notification) => ListSignals().Any(item =>
        (notification.Kind == NotificationKind.Recurring ? item.Current?.Id == notification.Subjects[0].Key.Id : item.Id == notification.Subjects[0].Key.Id) &&
        item.Enabled && item.Revision.ToString() == notification.SourceId && item.SnoozedUntil is null);

    private static SignalReminder MaterializeDebt(SignalReminder item, DateTimeOffset now) =>
        item.Current is null && item.NextAt is { } due && due <= now
            ? item with { Current = new(Guid.NewGuid(), due, now) } : item;

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
        if (item.Current is { } current) TaskNotificationsChanged?.Invoke(new("recurring", current.Id), false);
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
        WriteSignal(connection, transaction, item with { Enabled = false, NextAt = item.RequiresCompletion ? item.NextAt : null, SnoozedUntil = null, Revision = Guid.NewGuid() });
        command.CommandText = "DELETE FROM deleted_item WHERE slot=1";
        command.ExecuteNonQuery();
        transaction.Commit();
        return new("signal", item.Id);
    }
}
