using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Notifs.Core;

public sealed record TaskSession(Guid TaskId, DateTimeOffset StartedAt, long ActiveTicks,
    DateTimeOffset? RunningSince, DateTimeOffset? PausedAt = null, bool Finished = false,
    long? TargetActiveTicks = null, bool QuestionAsked = false, Guid? QuestionId = null);

public sealed partial class TaskService
{
    public TimeSpan ActiveTime(TaskSession session) => ActiveTimeAt(session, time.GetUtcNow());
    private static TimeSpan ActiveTimeAt(TaskSession session, DateTimeOffset now) => TimeSpan.FromTicks(session.ActiveTicks) +
        (session.RunningSince is { } since ? PositiveElapsed(since, now) : TimeSpan.Zero);
    private static TimeSpan PositiveElapsed(DateTimeOffset start, DateTimeOffset end) => end > start ? end - start : TimeSpan.Zero;
    private static TaskSession Accumulate(TaskSession session, DateTimeOffset now, bool pause) => session with
    {
        ActiveTicks = session.ActiveTicks + (session.RunningSince is { } since ? PositiveElapsed(since, now).Ticks : 0),
        RunningSince = pause ? null : now, PausedAt = pause ? now : null
    };
    private static IReadOnlyList<TaskSession> ReadSessions(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT data FROM task_sessions";
        using var reader = command.ExecuteReader(); var sessions = new List<TaskSession>();
        while (reader.Read()) sessions.Add(JsonSerializer.Deserialize<TaskSession>(reader.GetString(0))!);
        return sessions;
    }
    public IReadOnlyList<TaskSession> ReadSessions() { using var connection = Open(); return ReadSessions(connection, null); }
    public TaskSession? ReadSession(Guid id) => ReadSessions().SingleOrDefault(x => x.TaskId == id);
    private static void WriteSession(SqliteConnection connection, SqliteTransaction transaction, TaskSession session)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO task_sessions(task_id,data) VALUES($id,$data) ON CONFLICT(task_id) DO UPDATE SET data=excluded.data";
        command.Parameters.AddWithValue("$id", session.TaskId.ToString()); command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(session)); command.ExecuteNonQuery();
    }
    private static int? ReadEstimate(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT estimate_minutes,completed_at FROM tasks WHERE id=$id"; command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new KeyNotFoundException("Задача больше не существует.");
        return reader.IsDBNull(0) ? null : reader.GetInt32(0);
    }
    public TaskSession StartSession(Guid id) => ActivateSession(id, false);
    public TaskSession ResumeSession(Guid id) => ActivateSession(id, true);
    private TaskSession ActivateSession(Guid id, bool resume)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        var estimate = ReadEstimate(connection, transaction, id);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction; command.CommandText = "SELECT completed_at FROM tasks WHERE id=$id"; command.Parameters.AddWithValue("$id", id.ToString());
            if (command.ExecuteScalar() is string) throw new ArgumentException("Выполненную задачу сначала верните в работу.");
        }
        var sessions = ReadSessions(connection, transaction); var current = sessions.SingleOrDefault(x => x.TaskId == id);
        if (current is { RunningSince: not null }) return current;
        if (!resume && current is { Finished: false }) return current;
        if (resume && (current == null || current.Finished)) throw new ArgumentException("Сначала начните занятие.");
        var now = time.GetUtcNow();
        foreach (var other in sessions.Where(x => x.RunningSince != null)) WriteSession(connection, transaction, Accumulate(other, now, true));
        var active = resume ? current! with { RunningSince = now, PausedAt = null } : new TaskSession(id, now, 0, now,
            TargetActiveTicks: estimate is { } minutes ? TimeSpan.FromMinutes(minutes).Ticks : null, QuestionId: Guid.NewGuid());
        WriteSession(connection, transaction, active); transaction.Commit(); return active;
    }
    public void PauseSession(Guid id)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        var session = ReadSessions(connection, transaction).SingleOrDefault(x => x.TaskId == id);
        if (session?.RunningSince != null) WriteSession(connection, transaction, Accumulate(session, time.GetUtcNow(), true));
        transaction.Commit();
    }
    public void CancelSession(Guid id)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM task_sessions WHERE task_id=$id"; command.Parameters.AddWithValue("$id", id.ToString()); command.ExecuteNonQuery();
        CheckInChanged?.Invoke(id);
    }
    public event Action<Guid>? CheckInChanged;
    public void CheckpointSessions(bool pause = false)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction(); var now = time.GetUtcNow();
        foreach (var session in ReadSessions(connection, transaction).Where(x => x.RunningSince != null))
            WriteSession(connection, transaction, Accumulate(session, now, pause));
        transaction.Commit();
    }
    public void RecoverSessions()
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        foreach (var session in ReadSessions(connection, transaction).Where(x => x.RunningSince != null))
            WriteSession(connection, transaction, session with { RunningSince = null, PausedAt = time.GetUtcNow() });
        transaction.Commit();
    }
    private void FinishSession(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        var session = ReadSessions(connection, transaction).SingleOrDefault(x => x.TaskId == id);
        if (session != null && !session.Finished) WriteSession(connection, transaction, Accumulate(session, time.GetUtcNow(), true) with { Finished = true });
    }
}
