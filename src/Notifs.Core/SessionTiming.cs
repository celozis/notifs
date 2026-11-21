namespace Notifs.Core;

public sealed record TaskSessionTiming(TimeSpan Active, TimeSpan? Remaining, DateTimeOffset? QuestionAt);

public sealed partial class TaskService
{
    public TaskSessionTiming SessionTiming(TaskSession session) => SessionTimingAt(session, time.GetUtcNow());
    private static TaskSessionTiming SessionTimingAt(TaskSession session, DateTimeOffset now)
    {
        var active = ActiveTimeAt(session, now);
        TimeSpan? remaining = !session.Finished && !session.QuestionAsked && session.TargetActiveTicks is { } target
            ? TimeSpan.FromTicks(Math.Max(0, target - active.Ticks)) : null;
        var at = session.RunningSince != null && remaining is { } rest && rest.Ticks <= DateTimeOffset.MaxValue.UtcTicks - now.UtcTicks ? now.Add(rest) : (DateTimeOffset?)null;
        return new(active, remaining, at);
    }
}
