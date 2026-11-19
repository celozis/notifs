using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Notifs.Core;

public enum ImportanceRank { S, A, B, C, D }

// Date and optional wall-clock time remain distinct, including after persistence.
public sealed record TaskDeadline(DateOnly Date, TimeOnly? Time = null)
{
    public DateTimeOffset Resolve(TimeZoneInfo zone)
    {
        if (Time is null && Date == DateOnly.MaxValue) throw new ArgumentException("Выберите дату до 31.12.9999.");
        var local = Time is { } exact ? Date.ToDateTime(exact) : Date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        if (zone.IsInvalidTime(local)) throw new ArgumentException("Это время пропущено при переводе часов. Выберите другое время.");
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}

public sealed record TaskItem(Guid Id, string Title, string Description, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt,
    TaskDeadline? Deadline = null, int? EstimateMinutes = null, ImportanceRank Rank = ImportanceRank.B);

public sealed record TaskTiming(DateTimeOffset LocalNow, DateTimeOffset LocalCreatedAt, DateTimeOffset? DeadlineAt, TimeSpan? Slack, bool IsOverdue);

/// <summary>Application boundary. Each operation owns a connection; persisted timestamps use UTC.</summary>
public sealed class TaskService(string databasePath, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private const string Columns = "id,title,description,created_at,completed_at,deadline_date,deadline_time,estimate_minutes,rank";
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath), ForeignKeys = true
    }.ToString();

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version > 2) throw new IOException("Эта база создана более новой версией Notifs.");
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS tasks (
              id TEXT PRIMARY KEY, title TEXT NOT NULL CHECK(length(trim(title)) > 0),
              description TEXT NOT NULL, created_at TEXT NOT NULL, completed_at TEXT NULL
            );
            """;
        command.ExecuteNonQuery();
        if (version < 2)
        {
            command.CommandText = """
                ALTER TABLE tasks ADD COLUMN deadline_date TEXT NULL;
                ALTER TABLE tasks ADD COLUMN deadline_time TEXT NULL CHECK(deadline_time IS NULL OR deadline_date IS NOT NULL);
                ALTER TABLE tasks ADD COLUMN estimate_minutes INTEGER NULL CHECK(estimate_minutes IS NULL OR estimate_minutes > 0);
                ALTER TABLE tasks ADD COLUMN rank INTEGER NOT NULL DEFAULT 2 CHECK(rank BETWEEN 0 AND 4);
                PRAGMA user_version = 2;
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public IReadOnlyList<TaskItem> List(bool completed = false) => ReadTasks(completed);

    public IReadOnlyList<TaskItem> ListAll() => ReadTasks(null);

    private IReadOnlyList<TaskItem> ReadTasks(bool? completed)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM tasks" + (completed is null ? "" : completed.Value ? " WHERE completed_at IS NOT NULL" : " WHERE completed_at IS NULL");
        using var reader = command.ExecuteReader();
        var result = new List<TaskItem>();
        while (reader.Read()) result.Add(ReadItem(reader));
        return Order(result);
    }

    public TaskItem Create(string title, string description, TaskDeadline? deadline = null, int? estimateMinutes = null, ImportanceRank rank = ImportanceRank.B)
    {
        ValidateFields(deadline, estimateMinutes, rank);
        var item = new TaskItem(Guid.NewGuid(), ValidateTitle(title), description, time.GetUtcNow(), null, deadline, estimateMinutes, rank);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO tasks(id,title,description,created_at,deadline_date,deadline_time,estimate_minutes,rank) VALUES($id,$title,$description,$created,$date,$time,$estimate,$rank)";
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$description", item.Description);
        command.Parameters.AddWithValue("$created", FormatTime(item.CreatedAt));
        BindFields(command, deadline, estimateMinutes, rank);
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

    public void Edit(Guid id, string title, string description, TaskDeadline? deadline, int? estimateMinutes, ImportanceRank rank)
    {
        var validTitle = ValidateTitle(title);
        ValidateFields(deadline, estimateMinutes, rank);
        Update(id, "UPDATE tasks SET title=$title,description=$description,deadline_date=$date,deadline_time=$time,estimate_minutes=$estimate,rank=$rank WHERE id=$id", command =>
        {
            command.Parameters.AddWithValue("$title", validTitle);
            command.Parameters.AddWithValue("$description", description);
            BindFields(command, deadline, estimateMinutes, rank);
        });
    }

    private void ValidateFields(TaskDeadline? deadline, int? estimate, ImportanceRank rank)
    {
        if (estimate is <= 0) throw new ArgumentException("Полная оценка должна быть положительной.");
        if (!Enum.IsDefined(rank)) throw new ArgumentException("Выберите ранг S, A, B, C или D.");
        deadline?.Resolve(time.LocalTimeZone);
    }

    private static void BindFields(SqliteCommand command, TaskDeadline? deadline, int? estimate, ImportanceRank rank)
    {
        command.Parameters.AddWithValue("$date", (object?)deadline?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$time", (object?)deadline?.Time?.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$estimate", (object?)estimate ?? DBNull.Value);
        command.Parameters.AddWithValue("$rank", (int)rank);
    }

    public TaskItem Complete(Guid id) => Update(id,
        "UPDATE tasks SET completed_at=COALESCE(completed_at,$completed) WHERE id=$id",
        command => command.Parameters.AddWithValue("$completed", FormatTime(time.GetUtcNow())));

    public TaskItem Restore(Guid id) => Update(id, "UPDATE tasks SET completed_at=NULL WHERE id=$id", _ => { });

    private TaskItem Update(Guid id, string sql, Action<SqliteCommand> bind)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql + " RETURNING " + Columns;
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
        ParseTime(reader.GetString(3)), reader.IsDBNull(4) ? null : ParseTime(reader.GetString(4)),
        reader.IsDBNull(5) ? null : new(DateOnly.ParseExact(reader.GetString(5), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.IsDBNull(6) ? null : TimeOnly.Parse(reader.GetString(6), CultureInfo.InvariantCulture)),
        reader.IsDBNull(7) ? null : reader.GetInt32(7), (ImportanceRank)reader.GetInt32(8));

    public TaskTiming Timing(TaskItem item) => Timing(item, time.GetUtcNow());

    private TaskTiming Timing(TaskItem item, DateTimeOffset now)
    {
        var deadline = item.Deadline?.Resolve(time.LocalTimeZone);
        return new(TimeZoneInfo.ConvertTime(now, time.LocalTimeZone), TimeZoneInfo.ConvertTime(item.CreatedAt, time.LocalTimeZone),
            deadline, deadline - now - TimeSpan.FromMinutes(item.EstimateMinutes ?? 0), item.CompletedAt is null && deadline <= now);
    }

    public IReadOnlyList<TaskItem> Order(IEnumerable<TaskItem> items)
    {
        var now = time.GetUtcNow();
        return items.Select(item => (Item: item, Timing: Timing(item, now)))
            .OrderBy(x => x.Item.CompletedAt is not null ? 3 : x.Timing.IsOverdue ? 0 : x.Timing.DeadlineAt is not null ? 1 : 2)
            .ThenBy(x => x.Timing.IsOverdue ? x.Timing.DeadlineAt : null)
            .ThenBy(x => x.Item.CompletedAt is null && !x.Timing.IsOverdue ? x.Timing.Slack : null)
            .ThenBy(x => x.Item.CompletedAt is null ? x.Timing.DeadlineAt : null)
            .ThenBy(x => x.Item.CompletedAt is null && x.Item.Deadline is null ? x.Item.CreatedAt : DateTimeOffset.MinValue)
            .ThenBy(x => x.Item.CompletedAt is null ? x.Item.Rank : ImportanceRank.B)
            .ThenByDescending(x => x.Item.CompletedAt)
            .ThenBy(x => x.Item.Id).Select(x => x.Item).ToArray();
    }
}
