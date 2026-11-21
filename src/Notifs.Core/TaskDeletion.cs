using System.Text.Json;

namespace Notifs.Core;

public sealed partial class TaskService
{
    private sealed record DeletedTask(TaskItem Item, TaskDraft? Draft, PersonalReminderState? Personal = null, TaskSession? Session = null);

    public string? DeletedTitle
    {
        get
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT data FROM deleted_item WHERE slot=1 AND kind='task'";
            return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<DeletedTask>(json)?.Item.Title : null;
        }
    }

    public void DeleteTask(Guid id)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT " + Columns + " FROM tasks WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        TaskItem item;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) throw new KeyNotFoundException("Задача больше не существует.");
            item = ReadItem(reader);
        }
        command.CommandText = "SELECT data FROM task_drafts WHERE draft_key=$id";
        var draft = command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<TaskDraft>(json) : null;
        var personal = ReadPersonal(connection, transaction, id);
        var session = ReadSessions(connection, transaction).SingleOrDefault(x => x.TaskId == id);
        if (session != null) session = Accumulate(session, time.GetUtcNow(), true);
        command.CommandText = """
            INSERT INTO deleted_item(slot,kind,data) VALUES(1,'task',$data) ON CONFLICT(slot) DO UPDATE SET kind=excluded.kind,data=excluded.data;
            DELETE FROM task_drafts WHERE draft_key=$id;
            DELETE FROM tasks WHERE id=$id;
            """;
        command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(new DeletedTask(item, draft, personal, session)));
        command.ExecuteNonQuery();
        transaction.Commit();
        TaskNotificationsChanged?.Invoke(new("task", id), false);
    }

    public TaskItem RestoreDeleted()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT data FROM deleted_item WHERE slot=1 AND kind='task'";
        if (command.ExecuteScalar() is not string json) throw new KeyNotFoundException("Нет удалённой задачи для восстановления.");
        var deleted = JsonSerializer.Deserialize<DeletedTask>(json)!;
        var item = deleted.Item;
        command.CommandText = "INSERT INTO tasks(" + Columns + ") VALUES($id,$title,$description,$created,$completed,$date,$time,$estimate,$rank)";
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$description", item.Description);
        command.Parameters.AddWithValue("$created", FormatTime(item.CreatedAt));
        command.Parameters.AddWithValue("$completed", (object?)item.CompletedAt?.ToString("O") ?? DBNull.Value);
        BindFields(command, item.Deadline, item.EstimateMinutes, item.Rank);
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        if (deleted.Draft is { } draft)
        {
            command.CommandText = "INSERT INTO task_drafts(draft_key,data) VALUES($id,$data)";
            command.Parameters.AddWithValue("$id", item.Id.ToString());
            command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(draft));
            command.ExecuteNonQuery();
        }
        command.CommandText = "DELETE FROM deleted_item WHERE slot=1";
        command.Parameters.Clear();
        command.ExecuteNonQuery();
        if (deleted.Personal is { } personal) WritePersonal(connection, transaction, item.Id, personal);
        if (deleted.Session is { } session) WriteSession(connection, transaction, session with { RunningSince = null });
        transaction.Commit();
        return item;
    }
}
