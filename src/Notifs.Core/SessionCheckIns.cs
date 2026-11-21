using Microsoft.Data.Sqlite;

namespace Notifs.Core;

public sealed partial class TaskService
{
    public DateTimeOffset LocalNow => LocalTime(time.GetUtcNow());
    public DateTimeOffset LocalTime(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, time.LocalTimeZone);
    public DateTimeOffset? CheckInAt(Guid id)
    {
        return CheckInAt(ReadSession(id), time.GetUtcNow());
    }
    private static DateTimeOffset? CheckInAt(TaskSession? session, DateTimeOffset now)
    {
        return session == null ? null : SessionTimingAt(session, now).QuestionAt;
    }
    private static void ResetQuestion(SqliteConnection connection, SqliteTransaction transaction, Guid id, int? estimate)
    {
        var session = ReadSessions(connection, transaction).SingleOrDefault(x => x.TaskId == id);
        if (session is { Finished: false }) WriteSession(connection, transaction, session with
        { TargetActiveTicks = estimate is { } minutes ? TimeSpan.FromMinutes(minutes).Ticks : null, QuestionAsked = false, QuestionId = Guid.NewGuid() });
    }
    public IReadOnlyList<NotificationEvent> PollCheckIns()
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        var result = PollCheckIns(connection, transaction, time.GetUtcNow());
        transaction.Commit(); return result;
    }
    private IReadOnlyList<NotificationEvent> PollCheckIns(SqliteConnection connection, SqliteTransaction transaction, DateTimeOffset now)
    {
        var result = new List<NotificationEvent>();
        foreach (var session in ReadSessions(connection, transaction).Where(x => !x.Finished && x.RunningSince != null && !x.QuestionAsked && x.TargetActiveTicks != null))
        {
            var active = ActiveTime(session); if (active.Ticks < session.TargetActiveTicks) continue;
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT title FROM tasks WHERE id=$id AND completed_at IS NULL"; command.Parameters.AddWithValue("$id", session.TaskId.ToString());
            if (command.ExecuteScalar() is not string title) continue;
            var questionId = session.QuestionId ?? Guid.NewGuid();
            WriteSession(connection, transaction, session with { QuestionAsked = true, QuestionId = questionId });
            result.Add(new(questionId, NotificationKind.CheckIn, questionId.ToString(),
                [new(new("task", session.TaskId), title, $"Активное время — {(long)active.TotalMinutes} мин, без пауз.")],
                [new("complete", "Готово"), new("extend-check-in", "Ещё 30 минут")]));
        }
        return result;
    }
    public bool IsCheckInCurrent(NotificationEvent notification)
    {
        var session = ReadSession(notification.Subjects[0].Key.Id);
        return session is { Finished: false, QuestionAsked: true } && session.QuestionId.ToString() == notification.SourceId;
    }
    public void ExtendCheckIn(Guid id, string questionId)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        var session = ReadSessions(connection, transaction).SingleOrDefault(x => x.TaskId == id);
        if (session is not { Finished: false, QuestionAsked: true } || session.QuestionId.ToString() != questionId)
            throw new KeyNotFoundException("Этот вопрос уже обработан.");
        WriteSession(connection, transaction, session with { TargetActiveTicks = ActiveTime(session).Ticks + TimeSpan.FromMinutes(30).Ticks,
            QuestionAsked = false, QuestionId = Guid.NewGuid() });
        transaction.Commit(); CheckInChanged?.Invoke(id);
    }
}
