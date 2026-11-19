using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Notifs.Core;

public sealed record TaskItem(Guid Id, string Title, string Description, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

/// <summary>Application boundary. Each operation owns a connection; persisted timestamps use UTC.</summary>
public sealed class TaskService(string databasePath, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath), ForeignKeys = true
    }.ToString();

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS tasks (
              id TEXT PRIMARY KEY, title TEXT NOT NULL CHECK(length(trim(title)) > 0),
              description TEXT NOT NULL, created_at TEXT NOT NULL, completed_at TEXT NULL
            );
            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<TaskItem> List(bool completed = false) => ReadTasks(completed);

    public IReadOnlyList<TaskItem> ListAll() => ReadTasks(null);

    private IReadOnlyList<TaskItem> ReadTasks(bool? completed)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = completed is null
            ? "SELECT id,title,description,created_at,completed_at FROM tasks ORDER BY completed_at IS NOT NULL, CASE WHEN completed_at IS NULL THEN created_at END, completed_at DESC,id"
            : completed.Value
            ? "SELECT id,title,description,created_at,completed_at FROM tasks WHERE completed_at IS NOT NULL ORDER BY completed_at DESC,id"
            : "SELECT id,title,description,created_at,completed_at FROM tasks WHERE completed_at IS NULL ORDER BY created_at,id";
        using var reader = command.ExecuteReader();
        var result = new List<TaskItem>();
        while (reader.Read()) result.Add(ReadItem(reader));
        return result;
    }

    public TaskItem Create(string title, string description)
    {
        var item = new TaskItem(Guid.NewGuid(), ValidateTitle(title), description, time.GetUtcNow(), null);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO tasks(id,title,description,created_at) VALUES($id,$title,$description,$created)";
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$description", item.Description);
        command.Parameters.AddWithValue("$created", FormatTime(item.CreatedAt));
        command.ExecuteNonQuery();
        return item;
    }

    public void Edit(Guid id, string title, string description)
    {
        var validTitle = ValidateTitle(title);
        Update(id, "UPDATE tasks SET title=$title,description=$description WHERE id=$id", command =>
        {
            command.Parameters.AddWithValue("$title", validTitle);
            command.Parameters.AddWithValue("$description", description);
        });
    }

    public TaskItem Complete(Guid id) => Update(id,
        "UPDATE tasks SET completed_at=COALESCE(completed_at,$completed) WHERE id=$id",
        command => command.Parameters.AddWithValue("$completed", FormatTime(time.GetUtcNow())));

    public TaskItem Restore(Guid id) => Update(id, "UPDATE tasks SET completed_at=NULL WHERE id=$id", _ => { });

    private TaskItem Update(Guid id, string sql, Action<SqliteCommand> bind)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql + " RETURNING id,title,description,created_at,completed_at";
        command.Parameters.AddWithValue("$id", id.ToString());
        bind(command);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new KeyNotFoundException("Задача больше не существует. Обновите список.");
        var item = ReadItem(reader);
        reader.Read(); // Finish the statement before reporting a successful write.
        return item;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static string ValidateTitle(string title) => !string.IsNullOrWhiteSpace(title)
        ? title.Trim() : throw new ArgumentException("Введите название задачи. Описание останется на месте.", nameof(title));
    private static string FormatTime(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static TaskItem ReadItem(SqliteDataReader reader) => new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
        ParseTime(reader.GetString(3)), reader.IsDBNull(4) ? null : ParseTime(reader.GetString(4)));
}
