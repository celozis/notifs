using Notifs.Core;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using static Notifs.Scenarios.DraftScenarios;

namespace Notifs.Scenarios;

internal static class RecurringScenarios
{
    public static void Run(string directory)
    {
        var path = System.IO.Path.Combine(directory, "recurring.db");
        var clock = new SignalScenarios.Clock(new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
        var service = new TaskService(path, clock); service.Initialize();
        var weekly = new SignalSchedule(new(2026, 11, 23), new(15, 0), SignalRepeat.Weekly, [DayOfWeek.Monday], new(15, 0));
        var item = service.SaveSignal(null, "Сменить бельё", "", weekly, true, true, 60);
        Check(item.Current is null, "future recurring work cannot be completed early");
        clock.Set(new(2026, 12, 15, 10, 0, 0, TimeSpan.Zero));
        var notification = service.PollSignals().Single();
        item = service.ListSignals().Single();
        Check(item.Current?.DueAt == new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero) &&
            notification.Kind == NotificationKind.Recurring && service.PollSignals().Count == 0,
            "missed weeks produce one recurring debt at its first original date, one notification and no replay");
        var occurrence = item.Current!.Id;
        Check(service.RecurringAge(item.Current) == TimeSpan.FromDays(22), "recurring age is measured from first missed date rather than latest scheduled repeat");
        service.CompleteRecurring(occurrence);
        service.CompleteRecurring(occurrence);
        item = new TaskService(path, clock).ListSignals().Single();
        Check(item.Current is null && item.History.Count == 1 && item.History[0].DueAt == new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero) &&
            item.History[0].CompletedAt == new DateTimeOffset(2026, 12, 15, 10, 0, 0, TimeSpan.Zero) && item.NextAt == new DateTimeOffset(2026, 12, 21, 10, 0, 0, TimeSpan.Zero),
            "late completion is atomic and idempotent, retains original due and actual completion, fixed Mondays do not drift");
        service.SetSignalEnabled(item.Id, false);
        clock.Set(new(2027, 1, 12, 10, 0, 0, TimeSpan.Zero));
        Check(service.PollSignals().Count == 0, "disabled recurring schedule stays silent");
        item = service.SetSignalEnabled(item.Id, true);
        Check(item.Current?.DueAt == new DateTimeOffset(2026, 12, 21, 10, 0, 0, TimeSpan.Zero) && service.PollSignals().Count == 1 && service.PollSignals().Count == 0,
            "reenable creates one debt from first missed date and one notification");
        var interval = weekly with { Repeat = SignalRepeat.AfterCompletion, IntervalValue = 14 };
        var oldDebt = item.Current!;
        item = service.SaveSignal(item.Id, item.Title, "Новое правило", interval, true, true, 15);
        service.SetSignalEnabled(item.Id, false);
        Check(service.ListSignals().Single().Current is { } kept && kept.Id == oldDebt.Id && kept.DueAt == oldDebt.DueAt, "editing rule and disabling preserve original debt identity and date");
        service.CompleteRecurring(oldDebt.Id);
        item = service.ListSignals().Single();
        Check(!item.Enabled && item.Current is null && item.History.Count == 2 && item.NextAt == new DateTimeOffset(2027, 1, 26, 10, 0, 0, TimeSpan.Zero),
            "completion while disabled uses fourteen days after actual completion for the edited interval");
        service.SetSignalEnabled(item.Id, true);
        clock.Set(new(2027, 2, 9, 10, 0, 0, TimeSpan.Zero));
        var currentEvent = service.PollSignals().Single();
        service.SnoozeRecurring(currentEvent.Subjects[0].Key.Id);
        service = new(path, clock); service.Initialize();
        clock.Advance(TimeSpan.FromMinutes(29));
        Check(service.PollSignals().Count == 0 && !service.IsSignalCurrent(currentEvent), "persisted recurring snooze suppresses reminders and stale delivery for twenty-nine wall minutes");
        clock.Advance(TimeSpan.FromMinutes(1));
        Check(service.PollSignals().Count == 1 && service.ListSignals().Single().Current!.DueAt == new DateTimeOffset(2027, 1, 26, 10, 0, 0, TimeSpan.Zero),
            "ordinary thirty-minute snooze retains original debt date");
        clock.Advance(TimeSpan.FromMinutes(15));
        Check(service.PollSignals().Count == 1 && service.PollSignals().Count == 0, "hidden recurring cards do not cancel configured repeat notifications");
        item = service.ListSignals().Single();
        var beforeModeChange = item;
        try { service.SaveSignal(item.Id, "Сигнал", "Ввод", weekly, true); throw new Exception("debt cleared without confirmation"); }
        catch (DebtConfirmationRequiredException) { }
        Check(service.ListSignals().Single().Current == beforeModeChange.Current && service.ListSignals().Single().RequiresCompletion,
            "mode conversion rejects unconfirmed debt removal without changing saved rule");
        item = service.SaveSignal(item.Id, "Сигнал", "Ввод", weekly, true, confirmClearDebt: true);
        Check(!item.RequiresCompletion && item.Current is null && item.History.Count == 2, "confirmed conversion clears debt and preserves completion history");
        item = service.SaveSignal(item.Id, "Дело снова", "", interval, true, true, 15);
        service.PollSignals();
        var deletedDebt = service.ListSignals().Single().Current!;
        service.DeleteTask(service.Create("Удаление задачи", "").Id);
        service.DeleteSignal(item.Id);
        service = new(path, clock); service.Initialize();
        Check(service.ListSignals().Count == 0 && service.DeletedKind == "signal", "deleting confirmed schedule removes its debt and history and replaces the common slot");
        service.RestoreLastDeleted(); item = service.ListSignals().Single();
        Check(!item.Enabled && item.Current!.Id == deletedDebt.Id && item.Current.DueAt == deletedDebt.DueAt && item.History.Count == 2 && service.PollSignals().Count == 0,
            "restart restore returns original debt and history with disabled schedule and no visible replay");
        service.CompleteRecurring(occurrence);
        Check(service.ListSignals().Single().Current!.Id == deletedDebt.Id && service.ListSignals().Single().History.Count == 2, "stale occurrence completion cannot finish a different current debt");
        service.SetSignalEnabled(item.Id, true);
        var entries = service.ListReminderCalendar(new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), new(2027, 4, 1, 0, 0, 0, TimeSpan.Zero));
        Check(entries.Any(x => x.Kind == ReminderCalendarKind.Current && x.DueAt == deletedDebt.DueAt) &&
            entries.Any(x => x.Kind == ReminderCalendarKind.Completed && x.DueAt == new DateTimeOffset(2026, 12, 21, 10, 0, 0, TimeSpan.Zero) && x.CompletedAt == new DateTimeOffset(2027, 1, 12, 10, 0, 0, TimeSpan.Zero)) &&
            entries.All(x => x.Kind != ReminderCalendarKind.Planned), "calendar exposes original debt, actual completions with due dates, and no invented interval dates while debt is pending");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_complete BEFORE UPDATE ON signal_reminders BEGIN SELECT RAISE(ABORT,'blocked'); END"; command.ExecuteNonQuery();
        }
        try { service.CompleteRecurring(deletedDebt.Id); throw new Exception("blocked completion accepted"); }
        catch (SqliteException) { }
        item = service.ListSignals().Single();
        Check(item.Current?.Id == deletedDebt.Id && item.History.Count == 2, "failed completion atomically retains debt, history and next cycle");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TRIGGER fail_complete"; command.ExecuteNonQuery();
        }
        var hours = interval with { IntervalValue = 2, IntervalDays = false };
        service.SaveSignal(item.Id, item.Title, "", hours, true, true);
        service.CompleteRecurring(deletedDebt.Id);
        var planned = service.ListReminderCalendar(clock.GetUtcNow(), clock.GetUtcNow().AddDays(100)).Where(x => x.Kind == ReminderCalendarKind.Planned).ToArray();
        Check(planned.Length == 1 && planned[0].DueAt == new DateTimeOffset(2027, 2, 9, 12, 45, 0, TimeSpan.Zero), "two-hour completion interval projects only its nearest known future date");
        item = service.SaveSignal(item.Id, item.Title, "", hours with { IntervalValue = 1 }, true, true);
        Check(item.Current is null && item.NextAt == new DateTimeOffset(2027, 2, 9, 11, 45, 0, TimeSpan.Zero),
            "editing an interval after completion recalculates from actual completion without reviving its old first date");
        var hourly = weekly with { Repeat = SignalRepeat.Hourly, HourlyMinute = 25 };
        var fixedItem = service.SaveSignal(null, "Фиксированное дело", "", hourly, true, true);
        var future = service.ListReminderCalendar(clock.GetUtcNow(), clock.GetUtcNow().AddHours(3)).Where(x => x.ScheduleId == fixedItem.Id && x.Kind == ReminderCalendarKind.Planned).ToArray();
        Check(future.Length == 3 && future[0].DueAt == new DateTimeOffset(2027, 2, 9, 11, 25, 0, TimeSpan.Zero), "fixed hourly calendar exposes planned dates despite one outstanding debt");
        service.SetSignalEnabled(item.Id, false); service.SetSignalEnabled(fixedItem.Id, false);
        var disabledPlanned = service.ListReminderCalendar(clock.GetUtcNow(), clock.GetUtcNow().AddHours(3)).Where(x => x.Kind == ReminderCalendarKind.Planned).ToArray();
        Check(disabledPlanned.All(x => x.ScheduleId != fixedItem.Id && x.ScheduleId != item.Id),
            "disabled rules retain debt and history without active future calendar plans (#19)");
        service.SetSignalEnabled(fixedItem.Id, true);
        var signal = service.SaveSignal(fixedItem.Id, fixedItem.Title, "", hourly, true, confirmClearDebt: true);
        Check(signal.NextAt == new DateTimeOffset(2027, 2, 9, 11, 25, 0, TimeSpan.Zero), "conversion of an unchanged fixed rule to a signal skips old recurring debt rather than replaying it");

        var migrationPath = System.IO.Path.Combine(directory, "recurring-v5.db");
        var legacy = new TaskService(migrationPath, clock); legacy.Initialize();
        var task = legacy.Create("Старая задача", "Данные");
        var oldSignal = legacy.SaveSignal(null, "Старый сигнал", "", weekly, true);
        var oldJson = JsonSerializer.Serialize(new { oldSignal.Id, oldSignal.Title, oldSignal.Description, Schedule = new { weekly.FirstDate, weekly.FirstTime, weekly.Repeat, weekly.Days, weekly.WeeklyTime, weekly.HourlyMinute }, oldSignal.Enabled, oldSignal.NextAt, oldSignal.SnoozedUntil, oldSignal.Revision });
        using (var connection = new SqliteConnection($"Data Source={migrationPath}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE signal_reminders SET data=$data; INSERT INTO deleted_item VALUES(1,'signal',$data); PRAGMA user_version=5";
            command.Parameters.AddWithValue("$data", oldJson); command.ExecuteNonQuery();
        }
        legacy = new(migrationPath, clock); legacy.Initialize();
        Check(legacy.ListAll().Single() == task && legacy.ListSignals().Single() is { RequiresCompletion: false, Current: null } && legacy.ListSignals().Single().History.Count == 0,
            "v5 JSON migration preserves old tasks and signal semantics without manufacturing recurring debts");
        legacy.DeleteSignal(oldSignal.Id); legacy.RestoreLastDeleted();
        Check(!legacy.ListSignals().Single().Enabled && legacy.ListSignals().Single().History.Count == 0, "legacy signal still uses the shared disabled restoration after migration");
    }
}
