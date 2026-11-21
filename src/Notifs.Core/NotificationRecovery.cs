using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Notifs.Core;

public sealed partial class TaskService
{
    /// <summary>One transaction owns every cursor, undelivered subjects and the active-time checkpoint.</summary>
    public IReadOnlyList<NotificationEvent> PollNotifications(bool reconcile = false, bool suppress = false)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        var now = time.GetUtcNow();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT last_poll FROM runtime_checkpoint WHERE slot=1";
        var lastPoll = command.ExecuteScalar() is string last ? ParseTime(last) : (DateTimeOffset?)null;
        var unknownGap = lastPoll is { } previous && now - previous > TimeSpan.FromMinutes(1);
        reconcile |= unknownGap;
        if (reconcile)
        {
            command.CommandText = "DELETE FROM pending_notifications WHERE kind=$signal";
            command.Parameters.AddWithValue("$signal", (int)NotificationKind.Signal); command.ExecuteNonQuery(); command.Parameters.Clear();
        }
        foreach (var session in ReadSessions(connection, transaction).Where(x => x.RunningSince != null))
            WriteSession(connection, transaction, unknownGap && session.RunningSince <= lastPoll
                ? session with { RunningSince = null, PausedAt = now } : Accumulate(session, now, false));
        var personal = PollPersonal(connection, transaction, now);
        var signals = PollSignals(connection, transaction, now);
        var summary = PollSummary(connection, transaction, now);
        var questions = PollCheckIns(connection, transaction, now);
        var current = personal.Concat(signals).Concat(summary).Concat(questions).ToArray();
        var pending = new List<NotificationEvent>();
        if (!suppress)
        {
            command.CommandText = "SELECT data FROM pending_notifications";
            using (var reader = command.ExecuteReader()) while (reader.Read()) pending.Add(JsonSerializer.Deserialize<NotificationEvent>(reader.GetString(0))!);
            command.CommandText = "DELETE FROM pending_notifications"; command.ExecuteNonQuery();
        }
        var result = new List<NotificationEvent>();
        if (reconcile)
        {
            var missed = MissedMessage(pending.Where(x => x.Kind != NotificationKind.Signal).Concat(personal).Concat(signals.Where(x => x.Kind == NotificationKind.Recurring)));
            if (missed != null) result.Add(missed);
            result.AddRange(questions); // Explicitly started/resumed current sessions can legitimately become due.
        }
        else
        {
            var missed = MissedMessage(pending); if (missed != null) result.Add(missed);
            result.AddRange(current);
        }
        if (suppress) WritePending(connection, transaction, result);
        command.CommandText = "INSERT INTO runtime_checkpoint(slot,last_poll) VALUES(1,$now) ON CONFLICT(slot) DO UPDATE SET last_poll=excluded.last_poll";
        command.Parameters.AddWithValue("$now", FormatTime(now)); command.ExecuteNonQuery();
        transaction.Commit(); return result;
    }
    private static NotificationEvent? MissedMessage(IEnumerable<NotificationEvent> events)
    {
        var subjects = events.SelectMany(item => item.Subjects.Select(subject => subject with { Origin = subject.Origin ?? item.Kind, OriginId = subject.OriginId ?? item.SourceId }))
            .DistinctBy(x => (x.Key, x.Origin == NotificationKind.CheckIn)).ToArray();
        return subjects.Length == 0 ? null : new(Guid.NewGuid(), NotificationKind.Missed, "missed", subjects);
    }
    private static void WritePending(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<NotificationEvent> events)
    {
        foreach (var item in events)
        foreach (var subject in item.Subjects)
        {
            var kind = subject.Origin ?? item.Kind;
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO pending_notifications(kind,subject_type,subject_id,data) VALUES($kind,$type,$id,$data) ON CONFLICT(kind,subject_type,subject_id) DO UPDATE SET data=excluded.data";
            command.Parameters.AddWithValue("$kind", (int)kind); command.Parameters.AddWithValue("$type", subject.Key.Type); command.Parameters.AddWithValue("$id", subject.Key.Id.ToString());
            command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(item with { Kind = kind, SourceId = subject.OriginId ?? item.SourceId, Subjects = [subject] })); command.ExecuteNonQuery();
        }
    }
}
