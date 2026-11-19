using Notifs.Core;
using Microsoft.Data.Sqlite;
using static Notifs.Scenarios.DraftScenarios;

namespace Notifs.Scenarios;

internal static class SignalScenarios
{
    public static void Run(string directory)
    {
        var path = System.IO.Path.Combine(directory, "signals.db");
        var clock = new Clock(new(2026, 11, 22, 9, 59, 0, TimeSpan.Zero)); // Sunday 14:59 local.
        var service = new TaskService(path, clock);
        service.Initialize();
        var task = service.Create("Существующая задача", "не сигнал");
        var draft = new TaskDraft("незаконченная новая задача", "сохранить", "", "", "", "", ImportanceRank.B);
        service.SaveDraft(null, draft);
        var weeklyRule = new SignalSchedule(new(2026, 11, 22), new(0, 0), SignalRepeat.Weekly, [DayOfWeek.Monday], new(15, 0));
        var weekly = service.SaveSignal(null, "  Размяться  ", "Отдохнуть от экрана", weeklyRule, true);
        Check(weekly.Title == "Размяться" && weekly.NextAt == new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero), "Monday weekly rule skips Sunday and resolves Windows local 15:00");
        clock.Set(new(2026, 11, 23, 9, 59, 59, TimeSpan.Zero));
        Check(service.PollSignals().Count == 0, "weekly signal waits until the exact local instant");
        clock.Advance(TimeSpan.FromSeconds(1));
        var events = service.PollSignals();
        Check(events.Count == 1 && events[0].Kind == NotificationKind.Signal && events[0].Subjects.Single().Key == new EntityKey("signal", weekly.Id) && events[0].Actions!.All(x => x.Id != "complete"), "weekly occurrence emits a separate simple signal without completion action");
        Check(service.PollSignals().Count == 0, "reprocessing the same weekly boundary does not duplicate a signal");
        service = new(path, clock); service.Initialize();
        Check(service.PollSignals().Count == 0 && service.ListAll().Single() == task && service.ReadDraft(null) == draft, "signal state survives restart without contaminating ordinary tasks or drafts");
        var edited = service.SaveSignal(weekly.Id, "Изменённое название", "Новый текст", weeklyRule, true);
        Check(edited.Id == weekly.Id && edited.NextAt == new DateTimeOffset(2026, 11, 30, 10, 0, 0, TimeSpan.Zero) && service.PollSignals().Count == 0, "editing text preserves identity and consumed weekly occurrence");
        service.SetSignalEnabled(weekly.Id, false);
        var hourlyRule = new SignalSchedule(new(2026, 11, 23), new(15, 26), SignalRepeat.Hourly, [], new(0, 0), 25);
        var hourly = service.SaveSignal(null, "Каждый час", "Встать со стула", hourlyRule, true);
        Check(hourly.NextAt == new DateTimeOffset(2026, 11, 23, 11, 25, 0, TimeSpan.Zero), "first local instant is a lower bound, hourly minute aligns with wall clock");
        clock.Set(new(2026, 11, 23, 11, 25, 0, TimeSpan.Zero));
        Check(service.PollSignals().Count == 1 && service.PollSignals().Count == 0, "hourly :25 fires once at boundary");
        service.SetSignalEnabled(hourly.Id, false);
        clock.Advance(TimeSpan.FromHours(5));
        Check(service.PollSignals().Count == 0, "disabled signal does not deliver during absence");
        hourly = service.SetSignalEnabled(hourly.Id, true);
        Check(service.PollSignals().Count == 0 && hourly.NextAt == new DateTimeOffset(2026, 11, 23, 17, 25, 0, TimeSpan.Zero), "reenabling at an hourly boundary skips old signals and waits for a future instant");
        clock.Advance(TimeSpan.FromHours(1));
        var eventBeforeDisable = service.PollSignals().Single();
        Check(service.IsSignalCurrent(eventBeforeDisable), "live signal batch is eligible for native delivery");
        service.SaveNotificationSettings(new(false, 30));
        service.SnoozeSignal(hourly.Id);
        service = new(path, clock); service.Initialize();
        clock.Advance(TimeSpan.FromMinutes(29));
        Check(service.PollSignals().Count == 0 && !service.IsSignalCurrent(eventBeforeDisable), "persisted signal snooze suppresses old native batch for twenty-nine wall minutes");
        clock.Advance(TimeSpan.FromMinutes(1));
        Check(service.PollSignals().Count == 1 && service.ListSignals().Single(x => x.Id == hourly.Id).NextAt == new DateTimeOffset(2026, 11, 23, 18, 25, 0, TimeSpan.Zero), "ordinary snooze fires after thirty wall minutes and returns to :25 clock rule");
        service.SetSignalEnabled(hourly.Id, false);
        Check(!service.IsSignalCurrent(eventBeforeDisable), "disabling invalidates already-polled native events");
        var before = service.ListSignals().Count;
        foreach (var rule in new[] { weeklyRule with { Days = [] }, hourlyRule with { HourlyMinute = -1 }, hourlyRule with { HourlyMinute = 60 }, hourlyRule with { Repeat = (SignalRepeat)9 } })
        {
            try { service.SaveSignal(null, "Неверный", "ввод", rule, true); throw new Exception("invalid signal accepted"); }
            catch (ArgumentException) { }
        }
        try { service.SaveSignal(null, "  ", "ввод", weeklyRule, true); throw new Exception("blank signal accepted"); }
        catch (ArgumentException) { }
        Check(service.ListSignals().Count == before, "blank title, missing days, invalid minute or repeat cannot commit a rule");
        service.DeleteTask(task.Id);
        service.DeleteSignal(hourly.Id);
        service = new(path, clock); service.Initialize();
        Check(service.DeletedKind == "signal" && service.ListSignals().All(x => x.Id != hourly.Id), "confirmed signal deletion replaces task deletion in the single durable slot");
        var restored = service.RestoreLastDeleted();
        var restoredSignal = service.ListSignals().Single(x => x.Id == hourly.Id);
        Check(restored == new EntityKey("signal", hourly.Id) && !restoredSignal.Enabled && restoredSignal.SnoozedUntil is null && service.ListAll().Count == 0 && service.DeletedKind is null, "restart restoration returns only the last signal, disabled and without replay");
        service.DeleteSignal(hourly.Id);
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_signal_delete BEFORE DELETE ON signal_reminders BEGIN SELECT RAISE(ABORT,'write blocked'); END";
            command.ExecuteNonQuery();
        }
        try { service.DeleteSignal(weekly.Id); throw new Exception("blocked deletion accepted"); }
        catch (SqliteException) { }
        Check(service.ListSignals().Single().Id == weekly.Id && service.RestoreLastDeleted().Id == hourly.Id, "failed deletion preserves both live signal and previous restore slot atomically");
        Check(service.ReadDraft(null) == draft && service.ReadNotificationSettings() == new NotificationSettings(false, 30), "signal operations preserve task draft and global delivery settings");
        var midnight = new SignalSchedule(new(2026, 12, 31), new(23, 1), SignalRepeat.Hourly, [], new(), 0);
        var nextYear = service.SaveSignal(null, "Полночь", "", midnight, true);
        Check(nextYear.NextAt == new DateTimeOffset(2026, 12, 31, 19, 0, 0, TimeSpan.Zero), "hourly :00 crosses the local date and year boundary");
    }

    internal sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Signal UTC+5", TimeSpan.FromHours(5), "Test", "Test");
        public void Set(DateTimeOffset instant) => now = instant;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
