using Notifs.Core;

namespace Notifs.Scenarios;

internal static class NotificationScenarios
{
    public static void Run(string directory)
    {
        var path = System.IO.Path.Combine(directory, "notifications.db");
        var clock = new Clock(new DateTimeOffset(2026, 11, 20, 7, 0, 0, TimeSpan.Zero));
        var service = new TaskService(path, clock);
        service.Initialize();
        var task = service.SaveEditor(null, "Личное напоминание", "", null, 600, ImportanceRank.A,
            new PersonalSchedule(new DateOnly(2026, 11, 20), new TimeOnly(12, 1), 10));
        DraftScenarios.Check(service.PollPersonal().Count == 0, "personal schedule waits for first local instant");
        clock.Advance(TimeSpan.FromMinutes(1));
        var first = service.PollPersonal();
        DraftScenarios.Check(first.Count == 1 && first[0].Subjects.Single().Key.Id == task.Id && service.PollPersonal().Count == 0, "first personal event delivered once for repeated processing of the same instant");
        clock.Advance(TimeSpan.FromMinutes(10));
        DraftScenarios.Check(service.PollPersonal().Count == 1, "saved personal schedule repeats");
        service.SnoozePersonal(task.Id);
        var reopened = new TaskService(path, clock);
        reopened.Initialize();
        clock.Advance(TimeSpan.FromMinutes(29));
        DraftScenarios.Check(reopened.PollPersonal().Count == 0 && reopened.IsSnoozed(task.Id), "snooze persists and suppresses personal source before thirty wall minutes");
        clock.Advance(TimeSpan.FromMinutes(1));
        DraftScenarios.Check(reopened.PollPersonal().Count == 1 && !reopened.IsSnoozed(task.Id), "snoozed reminder fires at wall-clock deadline");
        DraftScenarios.Check(reopened.ListAll().Single().EstimateMinutes == 600 && reopened.ReadPersonal(task.Id)?.Schedule.RepeatMinutes == 10, "snooze preserves full estimate and base schedule");
        reopened.Complete(task.Id);
        clock.Advance(TimeSpan.FromHours(1));
        DraftScenarios.Check(reopened.PollPersonal().Count == 0, "completion stops personal notifications");
        var completed = reopened.ListAll().Single();
        var schedule = reopened.ReadPersonal(task.Id);
        reopened.DeleteTask(task.Id);
        DraftScenarios.Check(reopened.ReadPersonal(task.Id) is null, "deletion removes personal scheduling state");
        reopened.RestoreDeleted();
        DraftScenarios.Check(reopened.ReadPersonal(task.Id) == schedule && reopened.ListAll().Single() == completed, "restoring deletion preserves personal schedule and completion without replaying a visible stack");
        reopened.SaveNotificationSettings(new(false, 15));
        try { reopened.SaveNotificationSettings(new(true, 0)); throw new Exception("invalid settings saved"); }
        catch (ArgumentException) { }
        DraftScenarios.Check(new TaskService(path, clock).ReadNotificationSettings() == new NotificationSettings(false, 15), "invalid settings preserve previously committed sound and snooze together");
        var output = new Output();
        var delivery = new NotificationDelivery(output, clock);
        var a = first.Single();
        var b = a with { Id = Guid.NewGuid(), Kind = NotificationKind.Signal, SourceId = "signal" };
        var c = a with { Id = Guid.NewGuid(), Kind = NotificationKind.CheckIn, SourceId = "check-in" };
        delivery.Publish(a, true); delivery.Publish(b, true); delivery.Publish(c, true);
        DraftScenarios.Check(output.Stack.Select(x => x.Id).SequenceEqual(new[] { b.Id, c.Id }) && output.Sounds == 1, "A B C leaves B above C and throttles burst sound");
        delivery.Hide(b.Id);
        DraftScenarios.Check(output.Stack.Single().Id == c.Id, "hiding one card retains the other");
        delivery.Publish(a, true);
        DraftScenarios.Check(output.Stack.Single().Id == c.Id, "hidden or displaced events never return from a queue");
        var summary = a with { Id = Guid.NewGuid(), Kind = NotificationKind.Summary, Subjects = [a.Subjects.Single(), new(new("task", Guid.NewGuid()), "Другая задача", "")] };
        delivery.Publish(summary, false);
        delivery.RemoveSubject(new("task", task.Id), true);
        DraftScenarios.Check(output.Stack.Any(x => x.Id == c.Id) && output.Stack.Single(x => x.Kind == NotificationKind.Summary).Subjects.Single().Title == "Другая задача", "personal snooze removes summary row and preserves independent check-in");
        delivery.HideAll();
        DraftScenarios.Check(output.Stack.Count == 0 && output.Sounds == 1, "hide all clears visibility silently");
    }
    private sealed class Output : INotificationOutput
    {
        public IReadOnlyList<NotificationEvent> Stack { get; private set; } = [];
        public int Sounds { get; private set; }
        public void Render(IReadOnlyList<NotificationEvent> events) => Stack = events;
        public void PlaySound() => Sounds++;
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Test UTC+5", TimeSpan.FromHours(5), "Test", "Test");
        public void Advance(TimeSpan duration) => now += duration;
    }
}
