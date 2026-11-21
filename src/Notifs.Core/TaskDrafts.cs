using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Notifs.Core;

// Raw fields deliberately retain incomplete and invalid input, independently of saved tasks.
public sealed record TaskDraft(string Title, string Description, string DeadlineDate, string DeadlineTime,
    string EstimateHours, string EstimateMinutes, ImportanceRank Rank, bool PersonalEnabled = false,
    string PersonalDate = "", string PersonalTime = "", string RepeatMinutes = "");

public sealed partial class TaskService
{
    private static string DraftKey(Guid? id) => id?.ToString() ?? "new";

    public TaskDraft? ReadDraft(Guid? id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM task_drafts WHERE draft_key=$key";
        command.Parameters.AddWithValue("$key", DraftKey(id));
        return command.ExecuteScalar() is string data ? JsonSerializer.Deserialize<TaskDraft>(data) : null;
    }

    public void SaveDraft(Guid? id, TaskDraft draft)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO task_drafts(draft_key,data) VALUES($key,$data) ON CONFLICT(draft_key) DO UPDATE SET data=excluded.data";
        command.Parameters.AddWithValue("$key", DraftKey(id));
        command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(draft));
        command.ExecuteNonQuery();
    }

    public void DiscardDraft(Guid? id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM task_drafts WHERE draft_key=$key";
        command.Parameters.AddWithValue("$key", DraftKey(id));
        command.ExecuteNonQuery();
    }

    public TaskItem SaveEditor(Guid? id, string title, string description, TaskDeadline? deadline, int? estimate, ImportanceRank rank, PersonalSchedule? personal = null)
    {
        var validTitle = ValidateTitle(title);
        ValidateFields(deadline, estimate, rank);
        personal?.Resolve(time.LocalTimeZone);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var previousEstimate = id is { } originalId ? ReadEstimate(connection, transaction, originalId) : null;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = id is null
            ? "INSERT INTO tasks(id,title,description,created_at,deadline_date,deadline_time,estimate_minutes,rank) VALUES($id,$title,$description,$created,$date,$time,$estimate,$rank) RETURNING " + Columns
            : "UPDATE tasks SET title=$title,description=$description,deadline_date=$date,deadline_time=$time,estimate_minutes=$estimate,rank=$rank WHERE id=$id RETURNING " + Columns;
        command.Parameters.AddWithValue("$id", (id ?? Guid.NewGuid()).ToString());
        command.Parameters.AddWithValue("$title", validTitle);
        command.Parameters.AddWithValue("$description", description);
        if (id is null) command.Parameters.AddWithValue("$created", FormatTime(time.GetUtcNow()));
        BindFields(command, deadline, estimate, rank);
        TaskItem item;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) throw new KeyNotFoundException("Задача больше не существует.");
            item = ReadItem(reader);
            reader.Read();
        }
        command.Parameters.Clear();
        command.CommandText = "DELETE FROM task_drafts WHERE draft_key=$key";
        command.Parameters.AddWithValue("$key", DraftKey(id));
        command.ExecuteNonQuery();
        var personalChanged = SavePersonal(connection, transaction, item.Id, personal);
        if (id != null && previousEstimate != estimate) ResetQuestion(connection, transaction, item.Id, estimate);
        transaction.Commit();
        if (id != null && previousEstimate != estimate) CheckInChanged?.Invoke(item.Id);
        if (personalChanged) PersonalScheduleChanged?.Invoke(item.Id);
        return item;
    }
}
