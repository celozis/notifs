using Notifs.Core;

namespace Notifs.Scenarios;

internal static class SessionScenarios
{
    internal sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void Advance(TimeSpan span) => now += span;
    }
    public static void Run(string directory)
    {
        var clock = new Clock(new(2026, 11, 18, 12, 0, 0, TimeSpan.FromHours(5)));
        var path = System.IO.Path.Combine(directory, "sessions.db");
        var service = new TaskService(path, clock); service.Initialize();
        var a = service.Create("Практическая работа", "", estimateMinutes: 40);
        var b = service.Create("Без оценки", "");
        var started = service.StartSession(a.Id);
        clock.Advance(TimeSpan.FromMinutes(10)); service.PauseSession(a.Id);
        clock.Advance(TimeSpan.FromHours(1)); service.ResumeSession(a.Id);
        clock.Advance(TimeSpan.FromMinutes(5));
        DraftScenarios.Check(service.ActiveTime(service.ReadSession(a.Id)!) == TimeSpan.FromMinutes(15), "ten active, hour pause, five active accumulate fifteen minutes");
        service.StartSession(b.Id); service.StartSession(b.Id);
        DraftScenarios.Check(service.ReadSessions().Count(x => x.RunningSince != null) == 1 && service.ReadSession(a.Id)!.StartedAt == started.StartedAt, "starting B atomically pauses A and repeated start is idempotent");
        service.ResumeSession(a.Id);
        DraftScenarios.Check(service.ReadSession(b.Id)!.RunningSince == null && service.ListAll().Single(x => x.Id == a.Id).EstimateMinutes == 40, "resume preserves full estimate and pauses the previous active task");
        service.CheckpointSessions(); clock.Advance(TimeSpan.FromHours(5));
        var reopened = new TaskService(path, clock); reopened.Initialize(); reopened.RecoverSessions();
        DraftScenarios.Check(reopened.ReadSessions().All(x => x.RunningSince == null) && reopened.ActiveTime(reopened.ReadSession(a.Id)!) == TimeSpan.FromMinutes(15), "crash recovery pauses at the durable checkpoint without charging absence");
        reopened.ResumeSession(a.Id); clock.Advance(TimeSpan.FromMinutes(2));
        reopened.DeleteTask(a.Id); reopened.RestoreDeleted();
        DraftScenarios.Check(reopened.ReadSession(a.Id) is { RunningSince: null } restored && reopened.ActiveTime(restored) == TimeSpan.FromMinutes(17), "deleted session restores paused with accumulated active time");
        reopened.ResumeSession(a.Id); reopened.Complete(a.Id); reopened.Restore(a.Id);
        DraftScenarios.Check(reopened.ReadSession(a.Id) is { Finished: true } && reopened.StartSession(a.Id).ActiveTicks == 0, "completion retains last start, return does not resume, next start begins at zero");
        reopened.CancelSession(a.Id);
        DraftScenarios.Check(reopened.ReadSession(a.Id) == null && reopened.List().Any(x => x.Id == a.Id), "cancel removes the mistaken start and keeps the incomplete task");
        Parallel.Invoke(() => reopened.StartSession(a.Id), () => reopened.ResumeSession(b.Id));
        DraftScenarios.Check(reopened.ReadSessions().Count(x => x.RunningSince != null) == 1, "simultaneous public start/resume commands still commit exactly one running session");
    }
}
