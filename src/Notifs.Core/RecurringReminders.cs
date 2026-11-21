using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Notifs.Core;

public sealed class DebtConfirmationRequiredException() : ArgumentException("Подтвердите снятие текущего невыполненного дела при смене режима.");

public enum ReminderCalendarKind { Current, Completed, Planned }
public sealed record ReminderCalendarEntry(Guid ScheduleId, Guid? OccurrenceId, string Title, ReminderCalendarKind Kind,
    DateTimeOffset DueAt, DateTimeOffset? CompletedAt = null);

public sealed partial class TaskService
{
    public TimeSpan RecurringAge(RecurringOccurrence occurrence) => time.GetUtcNow() - occurrence.DueAt;
    // Half-open instant range; the calendar chooses local whole-day boundaries.
    public IReadOnlyList<ReminderCalendarEntry> ListReminderCalendar(DateTimeOffset from, DateTimeOffset until)
    {
        if (until <= from) throw new ArgumentException("Конец периода должен быть позже начала.");
        var result = new List<ReminderCalendarEntry>();
        var now = time.GetUtcNow();
        bool InRange(DateTimeOffset instant) => instant >= from && instant < until;
        foreach (var item in ListSignals())
        {
            if (item.Current is { } debt && InRange(debt.DueAt))
                result.Add(new(item.Id, debt.Id, item.Title, ReminderCalendarKind.Current, debt.DueAt));
            foreach (var done in item.History.Where(x => InRange(x.CompletedAt) || InRange(x.DueAt)))
                result.Add(new(item.Id, done.OccurrenceId, item.Title, ReminderCalendarKind.Completed, done.DueAt, done.CompletedAt));
            if (!item.RequiresCompletion || !item.Enabled) continue;
            if (item.Schedule.Repeat == SignalRepeat.AfterCompletion)
            {
                if (item.Current is null && item.NextAt is { } next && next > now && InRange(next))
                    result.Add(new(item.Id, null, item.Title, ReminderCalendarKind.Planned, next));
                continue;
            }
            var candidate = item.Schedule.Next(from > now ? from : now, time.LocalTimeZone, from > now);
            while (candidate is { } planned && planned < until)
            {
                result.Add(new(item.Id, null, item.Title, ReminderCalendarKind.Planned, planned));
                candidate = item.Schedule.Next(planned, time.LocalTimeZone);
            }
        }
        return result.OrderBy(x => x.CompletedAt ?? x.DueAt).ToArray();
    }

    // Occurrence identity deliberately differs from schedule identity: stale actions cannot complete a later cycle.
    private static SignalReminder? FindCurrent(SqliteConnection connection, SqliteTransaction transaction, Guid occurrenceId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT data FROM signal_reminders";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var item = JsonSerializer.Deserialize<SignalReminder>(reader.GetString(0))!;
            if (item.RequiresCompletion && item.Current?.Id == occurrenceId) return item;
        }
        return null;
    }

    public SignalReminder? CompleteRecurring(Guid occurrenceId)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var item = FindCurrent(connection, transaction, occurrenceId);
        if (item?.Current is not { } debt) return null;
        var now = time.GetUtcNow();
        var next = item.Schedule.Repeat == SignalRepeat.AfterCompletion ? now.Add(item.Schedule.CompletionInterval)
            : item.Schedule.Next(now, time.LocalTimeZone);
        var updated = item with { Current = null, SnoozedUntil = null, NextAt = next,
            Completions = [.. item.History, new(debt.Id, debt.DueAt, now)] };
        WriteSignal(connection, transaction, updated);
        transaction.Commit();
        TaskNotificationsChanged?.Invoke(new("recurring", debt.Id), false);
        return updated;
    }

    public SignalReminder? SnoozeRecurring(Guid occurrenceId)
    {
        var minutes = ReadNotificationSettings().SnoozeMinutes;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var item = FindCurrent(connection, transaction, occurrenceId);
        if (item is null) return null;
        var updated = item with { SnoozedUntil = time.GetUtcNow().AddMinutes(minutes) };
        WriteSignal(connection, transaction, updated);
        transaction.Commit();
        TaskNotificationsChanged?.Invoke(new("recurring", occurrenceId), false);
        return updated;
    }
}
