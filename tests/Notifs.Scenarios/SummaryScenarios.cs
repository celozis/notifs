using Notifs.Core;
using Microsoft.Data.Sqlite;

namespace Notifs.Scenarios;

internal static class SummaryScenarios
{
    public static void Run(string directory)
    {
        var path = System.IO.Path.Combine(directory, "summary.db");
        var clock = new Clock(new DateTimeOffset(2026, 11, 20, 7, 0, 0, TimeSpan.Zero));
        var service = new TaskService(path, clock);
        service.Initialize();
        var practice = service.Create("Практическая", "", new(new(2026, 11, 20), new(20, 0)), 420, ImportanceRank.D);
        var homework = service.Create("Домашка", "", new(new(2026, 11, 20), new(14, 0)), null, ImportanceRank.S);
        var course = service.Create("Курс", "", new(new(2026, 12, 20), new(12, 0)), 1200);
        var settings = new SummarySettings(true, 180, 3, new(2026, 11, 20), new(12, 1));
        service.SaveSettings(new(false, 15), settings);
        var reopened = new TaskService(path, clock);
        reopened.Initialize();
        Check(reopened.ReadSummarySettings() == settings && reopened.ReadNotificationSettings() == new NotificationSettings(false, 15), "summary and delivery settings persist together");
        Check(reopened.PollSummary().Count == 0, "summary waits for the chosen first wall time");
        clock.Advance(TimeSpan.FromMinutes(1));
        var first = reopened.PollSummary().Single();
        Check(first.Kind == NotificationKind.Summary && first.Subjects.Select(x => x.Key.Id).SequenceEqual(new[] { practice.Id, homework.Id, course.Id }), "summary uses slack urgency rather than rank");
        Check(first.Subjects[0].Rank == ImportanceRank.D && first.Subjects.All(x => x.Detail == "Создана сегодня"), "summary carries ranks and task ages");
        Check(reopened.PollSummary().Count == 0, "same summary instant is consumed once");
        CheckSnoozeAndSchedule(reopened, path, clock, practice, homework, course, settings);
        CheckHover(first, clock);
        CheckMigrationAndRollback(directory);
    }

    private static void CheckSnoozeAndSchedule(TaskService service, string path, Clock clock, TaskItem practice, TaskItem homework, TaskItem course, SummarySettings settings)
    {
        service.SaveEditor(practice.Id, practice.Title, "", practice.Deadline, practice.EstimateMinutes, practice.Rank,
            new(settings.FirstDate, new TimeOnly(12, 1), 60));
        service.SaveNotificationSettings(new(false, 240));
        service.SnoozePersonal(practice.Id);
        clock.Advance(TimeSpan.FromHours(3));
        service = new(path, clock); service.Initialize();
        var snoozed = service.PollSummary().Single();
        Check(snoozed.Subjects.Select(x => x.Key.Id).SequenceEqual(new[] { homework.Id, course.Id }), "personal snooze survives restart and excludes only that task from summary");
        service.SaveSettings(service.ReadNotificationSettings(), settings with { TaskCount = 1 });
        Check(service.PollSummary().Count == 0, "saving card limit does not replay consumed occurrence");
        clock.Advance(TimeSpan.FromHours(3));
        Check(service.PollSummary().Single().Subjects.Single().Key.Id == homework.Id, "expired snooze re-enters the changing urgency selection and N limits rows");
        service.Complete(homework.Id);
        clock.Advance(TimeSpan.FromHours(3));
        Check(service.PollSummary().Single().Subjects.Single().Key.Id == practice.Id, "completion between occurrences changes summary selection");
        service.Complete(practice.Id); service.Complete(course.Id);
        clock.Advance(TimeSpan.FromHours(3));
        Check(service.PollSummary().Count == 0, "empty summary consumes its tick without publishing a card");
        var later = service.Create("Без личного расписания", "", null, 600);
        Check(service.PollSummary().Count == 0, "creating after empty tick does not replay it");
        clock.Advance(TimeSpan.FromDays(7));
        Check(service.PollSummary().Count == 0 && service.PollSummary().Count == 0, "long downtime skips stale summaries without replaying a backlog");
        clock.Advance(TimeSpan.FromHours(3));
        var current = service.PollSummary().Single();
        Check(current.Subjects.Single().Key.Id == later.Id && current.Subjects.Single().Detail == "Создана 7 дн. назад", "fixed anchor resumes after downtime with current task age");
        var saved = service.ReadSummarySettings();
        foreach (var invalid in new[] { saved with { IntervalMinutes = 0 }, saved with { IntervalMinutes = int.MaxValue }, saved with { TaskCount = 0 }, saved with { TaskCount = 11 } })
        {
            try { service.SaveSettings(new(true, 99), invalid); throw new Exception("invalid summary accepted"); }
            catch (ArgumentException) { }
        }
        Check(service.ReadSummarySettings() == saved && service.ReadNotificationSettings() == new NotificationSettings(false, 240), "invalid summary preserves all previously saved settings");
        service.SaveSettings(new(false, 240), saved with { Enabled = false });
        clock.Advance(TimeSpan.FromHours(3));
        Check(service.PollSummary().Count == 0, "disabled summary produces no events");
        service.SaveSettings(new(false, 240), saved);
        Check(service.PollSummary().Single().Subjects.Single().Key.Id == later.Id, "reenabling at an anchored instant delivers the current tick");
        Check(service.PollSummary().Count == 0, "reenabled tick is still delivered once");
    }

    private static void CheckHover(NotificationEvent summary, TimeProvider clock)
    {
        var output = new Output();
        var delivery = new NotificationDelivery(output, clock);
        var checkin = summary with { Id = Guid.NewGuid(), Kind = NotificationKind.CheckIn };
        delivery.Publish(summary, true);
        delivery.SetHovered(true);
        delivery.Publish(checkin, true);
        Check(output.Stack.Single().Id == summary.Id, "new event waits while pointer is over shared stack");
        delivery.RemoveSubject(summary.Subjects[0].Key, true);
        Check(output.Stack.Single().Subjects.Count == 2, "explicit snooze updates visible summary while hovering");
        delivery.SetHovered(false);
        Check(output.Stack.Count == 2 && output.Stack.Last().Id == checkin.Id && output.Stack.Last().Subjects.Count == 3, "leaving hover publishes waiting independent check-in without suppressing it");
        var next = summary with { Id = Guid.NewGuid() };
        delivery.SetHovered(true); delivery.Publish(next, true);
        foreach (var subject in next.Subjects) delivery.RemoveSubject(subject.Key, false);
        delivery.SetHovered(false);
        Check(output.Stack.Count == 0, "completion removes subjects from visible and waiting summary; empty cards stay hidden");
        Check(output.Sounds == 1, "row invalidation and hover flush do not replay sound");
    }

    private sealed class Output : INotificationOutput
    {
        public IReadOnlyList<NotificationEvent> Stack { get; private set; } = [];
        public int Sounds { get; private set; }
        public void Render(IReadOnlyList<NotificationEvent> events) => Stack = events;
        public void PlaySound() => Sounds++;
    }

    private static void CheckMigrationAndRollback(string directory)
    {
        var path = System.IO.Path.Combine(directory, "summary-migration.db");
        var clock = new Clock(new DateTimeOffset(2026, 11, 20, 7, 0, 0, TimeSpan.Zero));
        var service = new TaskService(path, clock); service.Initialize();
        var task = service.SaveEditor(null, "Старая задача", "Сохранить", null, 600, ImportanceRank.A, new(new(2026, 11, 20), new(12, 0), 60));
        service.SaveNotificationSettings(new(false, 40));
        using var connection = new SqliteConnection($"Data Source={path}"); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE summary_settings; PRAGMA user_version=6"; command.ExecuteNonQuery();
        service = new(path, clock); service.Initialize();
        Check(service.List().Single() == task && service.ReadNotificationSettings() == new NotificationSettings(false, 40) && service.ReadPersonal(task.Id) is not null,
            "v6 migration preserves task fields, personal schedule and delivery settings");
        var settings = new SummarySettings(true, 1, 3, new(2026, 11, 20), new(12, 0));
        service.SaveSettings(new(false, 40), settings);
        command.CommandText = "CREATE TRIGGER fail_summary_poll BEFORE UPDATE ON summary_settings BEGIN SELECT RAISE(ABORT,'blocked'); END"; command.ExecuteNonQuery();
        try { service.PollSummary(); throw new Exception("poll write failure accepted"); }
        catch (SqliteException) { }
        command.CommandText = "DROP TRIGGER fail_summary_poll"; command.ExecuteNonQuery();
        var notification = service.PollSummary().Single();
        Check(notification.Subjects.Single().Key.Id == task.Id && service.PollSummary().Count == 0, "failed poll rolls back consumption and retries exactly once after storage recovers");
        service.SnoozePersonal(task.Id);
        Check(service.RefreshSummary(notification).Subjects.Count == 0, "polled batch is revalidated after a racing personal snooze");
        clock.Advance(TimeSpan.FromMinutes(40));
        Check(service.RefreshSummary(notification).Subjects.Count == 1, "expired personal snooze does not permanently suppress the task");
        service.Complete(task.Id);
        Check(service.RefreshSummary(notification).Subjects.Count == 0, "polled batch is revalidated after a racing completion");
    }

    private static void Check(bool condition, string message) => DraftScenarios.Check(condition, message);
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Summary UTC+5", TimeSpan.FromHours(5), "Test", "Test");
        public void Advance(TimeSpan amount) => now += amount;
    }
}
