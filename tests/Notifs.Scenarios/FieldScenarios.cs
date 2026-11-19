using Notifs.Core;

namespace Notifs.Scenarios;

internal static class FieldScenarios
{
    public static void Run(string directory)
    {
        var clock = new Clock(new DateTimeOffset(2026, 11, 18, 7, 0, 0, TimeSpan.Zero));
        var path = System.IO.Path.Combine(directory, "fields.db");
        var service = new TaskService(path, clock);
        service.Initialize();
        var empty = service.Create("Без необязательных полей", "");
        Check(empty.Deadline is null && empty.EstimateMinutes is null && empty.Rank == ImportanceRank.B,
            "new tasks keep absent deadline/estimate and default rank B");
        var item = service.Create("Практическая", "**Описание**", new(new(2026, 11, 18), new(20, 0)), 420, ImportanceRank.S);
        var reopened = new TaskService(path, clock);
        reopened.Initialize();
        var saved = reopened.List().Single(x => x.Id == item.Id);
        Check(saved.Deadline == item.Deadline && saved.EstimateMinutes == 420 && saved.Rank == ImportanceRank.S,
            "deadline, full estimate and rank survive SQLite reopen");
        var homework = service.Create("Домашка", "", new(new(2026, 11, 18), new(14, 0)));
        var course = service.Create("Курс", "", new(new(2026, 12, 18), new(12, 0)), 1200);
        Check(service.List().Select(x => x.Id).SequenceEqual(new[] { item.Id, homework.Id, course.Id, empty.Id }),
            "approved urgency example: practice, homework without estimate, course, no deadline");
        Check(service.Timing(item).Slack == TimeSpan.FromHours(1), "slack subtracts the full estimate");
        var endOfDay = service.Create("До конца дня", "", new(new(2026, 11, 18)));
        clock.Set(new(2026, 11, 18, 18, 59, 59, TimeSpan.Zero));
        Check(!service.Timing(endOfDay).IsOverdue, "date-only deadline includes last local second");
        clock.Set(new(2026, 11, 18, 19, 0, 0, TimeSpan.Zero));
        Check(service.Timing(endOfDay).IsOverdue, "date-only deadline expires at next local midnight");
        clock.Set(new(2026, 11, 18, 7, 0, 0, TimeSpan.Zero));
        service.Edit(item.Id, item.Title, item.Description, item.Deadline, 480, ImportanceRank.A);
        var zero = service.List().Single(x => x.Id == item.Id);
        Check(service.Timing(zero).Slack == TimeSpan.Zero && !service.Timing(zero).IsOverdue, "zero slack is valid and not overdue");
        service.Edit(item.Id, item.Title, item.Description, item.Deadline, 540, ImportanceRank.S);
        var negative = service.List().Single(x => x.Id == item.Id);
        Check(service.Timing(negative).Slack == TimeSpan.FromHours(-1) && !service.Timing(negative).IsOverdue,
            "negative slack before deadline remains distinct from overdue");
        clock.Set(new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero));
        Check(service.Timing(negative).IsOverdue, "exact deadline expires at its instant");
        Check(service.List().Take(2).Select(x => x.Id).SequenceEqual(new[] { homework.Id, item.Id }),
            "clock advancement moves overdue tasks above future deadlines by oldest deadline");
        service.Complete(item.Id);
        Check(!service.Timing(service.List(true).Single()).IsOverdue, "completed task is never overdue");
        service.Restore(item.Id);
        var originalCreation = item.CreatedAt;
        service.Edit(item.Id, "Уточнённая практическая", "Описание сохранено", null, null, ImportanceRank.D);
        var cleared = new TaskService(path, clock).List().Single(x => x.Id == item.Id);
        Check(cleared.Deadline is null && cleared.EstimateMinutes is null && cleared.Rank == ImportanceRank.D && cleared.CreatedAt == originalCreation,
            "clearing optional fields persists absence and preserves original creation");
        foreach (var estimate in new[] { 0, -1 })
        {
            try { service.Edit(item.Id, "Потерянный ввод", "", null, estimate, ImportanceRank.S); throw new Exception("invalid estimate accepted"); }
            catch (ArgumentException) { }
        }
        try { service.Create("Ранг вне диапазона", "", null, null, (ImportanceRank)5); throw new Exception("invalid rank accepted"); }
        catch (ArgumentException) { }
        Check(service.List().Single(x => x.Id == item.Id) == cleared, "invalid field edit preserves the saved task");
        Check(service.List().Single(x => x.Id == course.Id).EstimateMinutes == 1200, "time passing never reduces full estimate");
        StableOrdering(directory, clock);
        Migration(directory, clock);
    }

    private static void StableOrdering(string directory, Clock clock)
    {
        var path = System.IO.Path.Combine(directory, "ties.db");
        var tasks = new TaskService(path, clock);
        tasks.Initialize();
        var older = tasks.Create("Старая без срока", "", null, null, ImportanceRank.D);
        clock.Set(clock.GetUtcNow().AddHours(1));
        var low = tasks.Create("D", "", null, null, ImportanceRank.D);
        var high = tasks.Create("S", "", null, 4320, ImportanceRank.S);
        tasks.Create("Ещё S", "", null, null, ImportanceRank.S);
        var order = tasks.List().Select(x => x.Id).ToArray();
        Check(order[0] == older.Id && order[3] == low.Id, "no deadline sorts by creation then rank; calendar estimate never becomes deadline");
        Check(order.SequenceEqual(new TaskService(path, clock).List().Select(x => x.Id)), "equal keys keep stable ID ordering across restart");
        var sameSlackLater = tasks.Create("Позже, но важнее", "", new(new(2026, 11, 20), new(14, 0)), 120, ImportanceRank.S);
        var sameSlackSooner = tasks.Create("Ближе", "", new(new(2026, 11, 20), new(13, 0)), 60, ImportanceRank.D);
        Check(tasks.List().Take(2).Select(x => x.Id).SequenceEqual(new[] { sameSlackSooner.Id, sameSlackLater.Id }), "equal slack breaks by nearer deadline before rank");
        tasks.Edit(high.Id, "S со сроком", "", sameSlackSooner.Deadline, 60, ImportanceRank.S);
        Check(tasks.List().First().Id == high.Id, "equal slack and deadline break by rank");
    }

    private static void Migration(string directory, Clock clock)
    {
        var path = System.IO.Path.Combine(directory, "legacy.db");
        var id = Guid.NewGuid();
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE tasks(id TEXT PRIMARY KEY,title TEXT NOT NULL,description TEXT NOT NULL,created_at TEXT NOT NULL,completed_at TEXT NULL);
                INSERT INTO tasks VALUES($id,'Старая задача','**Markdown**','2026-11-17T05:00:00.0000000+00:00','2026-11-18T06:00:00.0000000+00:00');
                PRAGMA user_version=1;
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.ExecuteNonQuery();
        }
        var migrated = new TaskService(path, clock);
        migrated.Initialize(); migrated.Initialize();
        var legacy = migrated.ListAll().Single();
        Check(legacy.Id == id && legacy.Title == "Старая задача" && legacy.Description == "**Markdown**" && legacy.CompletedAt is not null
            && legacy.CreatedAt == new DateTimeOffset(2026, 11, 17, 5, 0, 0, TimeSpan.Zero) && legacy.Deadline is null && legacy.EstimateMinutes is null && legacy.Rank == ImportanceRank.B,
            "v1 migration preserves identity, text, creation and completion with absent fields and B rank");
        migrated.Edit(id, legacy.Title, legacy.Description, new(new(2026, 12, 1)), 90, ImportanceRank.C);
        Check(new TaskService(path, clock).ListAll().Single().EstimateMinutes == 90, "migrated database accepts and persists new fields");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Scenario UTC+5", TimeSpan.FromHours(5), "UTC+5", "UTC+5");
        public void Set(DateTimeOffset value) => now = value;
    }
}
