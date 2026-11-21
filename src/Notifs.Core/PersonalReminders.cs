using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Notifs.Core;

public sealed record PersonalSchedule(DateOnly Date, TimeOnly Time, int? RepeatMinutes = null)
{
    public DateTimeOffset Resolve(TimeZoneInfo zone)
    {
        if (RepeatMinutes is <= 0) throw new ArgumentException("Повтор должен быть положительным числом минут либо пустым.");
        return new TaskDeadline(Date, Time).Resolve(zone);
    }
}

public sealed record PersonalReminderState(PersonalSchedule Schedule, DateTimeOffset FirstAt, DateTimeOffset? NextAt, DateTimeOffset? SnoozedUntil = null);
public sealed record NotificationSettings(bool Sound = true, int SnoozeMinutes = 30);

public sealed partial class TaskService
{
    // false removes all sources for an entity; true removes personal + summary mentions only.
    public event Action<EntityKey, bool>? TaskNotificationsChanged;
    public event Action<Guid>? PersonalScheduleChanged;

    public PersonalReminderState? ReadPersonal(Guid id)
    {
        using var connection = Open();
        return ReadPersonal(connection, null, id);
    }

    private static PersonalReminderState? ReadPersonal(SqliteConnection connection, SqliteTransaction? transaction, Guid id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT data FROM personal_reminders WHERE task_id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<PersonalReminderState>(json) : null;
    }

    private static void WritePersonal(SqliteConnection connection, SqliteTransaction transaction, Guid id, PersonalReminderState state)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO personal_reminders(task_id,data) VALUES($id,$data) ON CONFLICT(task_id) DO UPDATE SET data=excluded.data";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(state));
        command.ExecuteNonQuery();
    }

    private bool SavePersonal(SqliteConnection connection, SqliteTransaction transaction, Guid id, PersonalSchedule? schedule)
    {
        var old = ReadPersonal(connection, transaction, id);
        if (old?.Schedule == schedule) return false; // Unrelated edits never restart a consumed occurrence/snooze.
        using (var pending = connection.CreateCommand())
        {
            pending.Transaction = transaction; pending.CommandText = "DELETE FROM pending_notifications WHERE kind=$kind AND subject_type='task' AND subject_id=$id";
            pending.Parameters.AddWithValue("$kind", (int)NotificationKind.Personal); pending.Parameters.AddWithValue("$id", id.ToString()); pending.ExecuteNonQuery();
        }
        if (schedule is null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM personal_reminders WHERE task_id=$id";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.ExecuteNonQuery();
        }
        else
        {
            var first = schedule.Resolve(time.LocalTimeZone);
            WritePersonal(connection, transaction, id, new(schedule, first, first, old?.SnoozedUntil));
        }
        return true;
    }

    public NotificationSettings ReadNotificationSettings()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sound,snooze_minutes FROM notification_settings WHERE slot=1";
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetBoolean(0), reader.GetInt32(1)) : new();
    }

    public void SaveNotificationSettings(NotificationSettings settings)
    {
        ValidateNotificationSettings(settings);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        WriteNotificationSettings(connection, transaction, settings);
        transaction.Commit();
    }

    private static void ValidateNotificationSettings(NotificationSettings settings)
    {
        if (settings.SnoozeMinutes is <= 0 or > 525600) throw new ArgumentException("Отсрочка: целое число от 1 до 525600 минут.");
    }

    private static void WriteNotificationSettings(SqliteConnection connection, SqliteTransaction transaction, NotificationSettings settings)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO notification_settings(slot,sound,snooze_minutes) VALUES(1,$sound,$minutes) ON CONFLICT(slot) DO UPDATE SET sound=excluded.sound,snooze_minutes=excluded.snooze_minutes";
        command.Parameters.AddWithValue("$sound", settings.Sound);
        command.Parameters.AddWithValue("$minutes", settings.SnoozeMinutes);
        command.ExecuteNonQuery();
    }

    public bool IsSnoozed(Guid id) => ReadPersonal(id)?.SnoozedUntil > time.GetUtcNow();

    public void SnoozePersonal(Guid id)
    {
        var until = time.GetUtcNow().AddMinutes(ReadNotificationSettings().SnoozeMinutes);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var state = ReadPersonal(connection, transaction, id) ?? throw new KeyNotFoundException("Личное расписание больше не существует.");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM tasks WHERE id=$id AND completed_at IS NULL";
        command.Parameters.AddWithValue("$id", id.ToString());
        if (Convert.ToInt32(command.ExecuteScalar()) == 0) throw new KeyNotFoundException("Задача уже выполнена или удалена.");
        WritePersonal(connection, transaction, id, state with { SnoozedUntil = until });
        transaction.Commit();
        TaskNotificationsChanged?.Invoke(new("task", id), true);
    }

    public IReadOnlyList<NotificationEvent> PollPersonal()
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        var result = PollPersonal(connection, transaction, time.GetUtcNow());
        transaction.Commit(); return result;
    }
    private IReadOnlyList<NotificationEvent> PollPersonal(SqliteConnection connection, SqliteTransaction transaction, DateTimeOffset now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT t.id,t.title,t.created_at,p.data FROM tasks t JOIN personal_reminders p ON p.task_id=t.id WHERE t.completed_at IS NULL ORDER BY t.created_at,t.id";
        var due = new List<(Guid Id, string Title, DateTimeOffset Created, PersonalReminderState State)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var state = JsonSerializer.Deserialize<PersonalReminderState>(reader.GetString(3))!;
                var next = state.SnoozedUntil ?? state.NextAt;
                if (next <= now) due.Add((Guid.Parse(reader.GetString(0)), reader.GetString(1), ParseTime(reader.GetString(2)), state));
            }
        }
        var events = new List<NotificationEvent>();
        foreach (var entry in due)
        {
            DateTimeOffset? next = null;
            if (entry.State.Schedule.RepeatMinutes is { } repeat)
            {
                var interval = TimeSpan.FromMinutes(repeat).Ticks;
                var steps = Math.Max(1, (now - entry.State.FirstAt).Ticks / interval + 1);
                var ticks = entry.State.FirstAt.UtcTicks + steps * interval;
                if (ticks <= DateTimeOffset.MaxValue.UtcTicks) next = new DateTimeOffset(ticks, TimeSpan.Zero);
            }
            WritePersonal(connection, transaction, entry.Id, entry.State with { NextAt = next, SnoozedUntil = null });
            var days = Math.Max(0, (TimeZoneInfo.ConvertTime(now, time.LocalTimeZone).Date - TimeZoneInfo.ConvertTime(entry.Created, time.LocalTimeZone).Date).Days);
            var age = days == 0 ? "Создана сегодня" : days == 1 ? "Создана вчера" : $"Создана {days} дн. назад";
            events.Add(new(Guid.NewGuid(), NotificationKind.Personal, entry.Id.ToString(), [new(new("task", entry.Id), entry.Title, age)],
                [new("complete", "Готово"), new("snooze", "Отложить")]));
        }
        return events;
    }
}
