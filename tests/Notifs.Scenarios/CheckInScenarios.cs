using Notifs.Core;

namespace Notifs.Scenarios;

internal static class CheckInScenarios
{
    public static void Run(string directory)
    {
        var clock = new SessionScenarios.Clock(new(2026, 11, 18, 7, 0, 0, TimeSpan.Zero));
        var service = new TaskService(System.IO.Path.Combine(directory, "check-in.db"), clock); service.Initialize();
        var task = service.Create("Отправить домашнюю работу", "", estimateMinutes: 40);
        service.StartSession(task.Id); clock.Advance(TimeSpan.FromMinutes(10)); service.PauseSession(task.Id);
        clock.Advance(TimeSpan.FromHours(1));
        DraftScenarios.Check(service.PollCheckIns().Count == 0 && service.CheckInAt(task.Id) == null, "pause removes wall check-in and never consumes active remainder");
        service.ResumeSession(task.Id); clock.Advance(TimeSpan.FromMinutes(30));
        var question = service.PollCheckIns().Single();
        DraftScenarios.Check(question.Kind == NotificationKind.CheckIn && service.PollCheckIns().Count == 0, "forty active minutes ask once, repeated polling never rearms a dismissed question");
        service.PauseSession(task.Id); service.ExtendCheckIn(task.Id, question.SourceId);
        clock.Advance(TimeSpan.FromHours(1)); service.ResumeSession(task.Id); clock.Advance(TimeSpan.FromMinutes(10));
        service.CheckpointSessions(); service.RecoverSessions();
        clock.Advance(TimeSpan.FromDays(1)); service.ResumeSession(task.Id); clock.Advance(TimeSpan.FromMinutes(19));
        DraftScenarios.Check(service.PollCheckIns().Count == 0, "extension during pause survives restart and still needs twenty further active minutes");
        clock.Advance(TimeSpan.FromMinutes(1)); service.PollCheckIns().Single();
        service.Edit(task.Id, task.Title + "!", "changed", null, 40, ImportanceRank.S);
        DraftScenarios.Check(service.PollCheckIns().Count == 0, "same estimate and unrelated fields never rearm a consumed question");
        service.Edit(task.Id, task.Title, "", null, 60, ImportanceRank.A);
        DraftScenarios.Check(service.PollCheckIns().Count == 1, "actually changed estimate below accumulated time asks immediately when running");
        service.Edit(task.Id, task.Title, "", null, null, ImportanceRank.A);
        DraftScenarios.Check(service.PollCheckIns().Count == 0 && service.CheckInAt(task.Id) == null && service.ReadSession(task.Id) is { Finished: false }, "removing estimate removes question and preserves session");
    }
}
