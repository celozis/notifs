using Notifs.Core;

namespace Notifs.Scenarios;

internal static class RecoveryScenarios
{
    public static void Run(string directory)
    {
        var clock = new SessionScenarios.Clock(new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var path = System.IO.Path.Combine(directory, "recovery.db"); var service = new TaskService(path, clock); service.Initialize();
        var task = service.SaveEditor(null, "Личная задача", "# Сохраняется", null, 40, ImportanceRank.S, new(new(2026, 11, 18), new(12, 1), 60));
        service.SaveSignal(null, "Разминка", "", new(new(2026, 11, 18), new(12, 1), SignalRepeat.Hourly, [], new(12, 1), 1), true);
        service.SaveSignal(null, "Долг", "", new(new(2026, 11, 18), new(12, 1), SignalRepeat.AfterCompletion, [], new(12, 1)), true, true);
        service.StartSession(task.Id); service.PollNotifications(); clock.Advance(TimeSpan.FromSeconds(10)); service.PollNotifications();
        clock.Advance(TimeSpan.FromDays(14));
        var result = service.PollNotifications(reconcile: true);
        DraftScenarios.Check(result.Count == 1 && result[0].Kind == NotificationKind.Missed && result[0].Subjects.Count == 2, "two weeks absence yields one current-deeds message without old hourly signals or summaries");
        DraftScenarios.Check(service.ActiveTime(service.ReadSession(task.Id)!) == TimeSpan.FromSeconds(10) && service.ReadSession(task.Id)!.RunningSince == null, "recovery excludes unknown time beyond the last successful checkpoint");
        DraftScenarios.Check(service.PollNotifications(reconcile: true).Count == 0 && service.ListSignals().Single(x => x.Title == "Долг").Current!.DueAt == new DateTimeOffset(2026, 11, 18, 12, 1, 0, TimeSpan.Zero), "repeated reconciliation preserves first debt and never repeats consumed misses");
        service.ResumeSession(task.Id); clock.Advance(TimeSpan.FromSeconds(5)); service.CheckpointSessions(true);
        clock.Advance(TimeSpan.FromDays(2)); var reopened = new TaskService(path, clock); reopened.Initialize(); reopened.RecoverSessions();
        DraftScenarios.Check(reopened.ActiveTime(reopened.ReadSession(task.Id)!) == TimeSpan.FromSeconds(15), "explicit exit/sleep checkpoint pauses and survives reopen without charging downtime");
        var fresh = reopened.Create("Начать после восстановления", ""); reopened.StartSession(fresh.Id);
        reopened.PollNotifications(reconcile: true);
        DraftScenarios.Check(reopened.ReadSession(fresh.Id)!.RunningSince != null, "initial reconciliation preserves an explicit new start after the old checkpoint");
        var next = clock.GetUtcNow().AddSeconds(10);
        var oneShot = reopened.SaveEditor(null, "Не потерять в fullscreen", "", null, null, ImportanceRank.B, new(DateOnly.FromDateTime(next.UtcDateTime), TimeOnly.FromDateTime(next.UtcDateTime)));
        clock.Advance(TimeSpan.FromSeconds(10)); reopened.PollNotifications(suppress: true);
        var restarted = new TaskService(path, clock); restarted.Initialize(); restarted.RecoverSessions();
        var recovered = restarted.PollNotifications(reconcile: true);
        DraftScenarios.Check(recovered.Single(x => x.Kind == NotificationKind.Missed).Subjects.Any(x => x.Key.Id == oneShot.Id), "one-shot fullscreen suppression survives a real storage reopen after its cursor is consumed");
        DraftScenarios.Check(restarted.PollNotifications(reconcile: true).Count == 0, "durable suppressed notification is reconciled exactly once");
    }
}
