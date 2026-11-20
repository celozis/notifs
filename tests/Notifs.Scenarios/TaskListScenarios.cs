using System.IO;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static class TaskListScenarios
{
    public static void Run(string directory)
    {
        var service = new TaskService(Path.Combine(directory, "lists.db"), new ListClock());
        service.Initialize();
        var a = service.Create("S 120", "", new(new(2026, 11, 20)), 120, ImportanceRank.S);
        var b = service.Create("A 60", "", new(new(2026, 11, 21)), 60, ImportanceRank.A);
        var c = service.Create("B без полей", "");
        service.Complete(a.Id);
        var settings = new TaskListSettings(Sort: TaskSort.Estimate, Descending: true);
        Check(service.QueryTasks(settings).Select(x => x.Id).SequenceEqual(new[] { b.Id, c.Id, a.Id }), "manual order keeps incomplete first and null estimates last within each group");
        settings = settings with { Ranks = [ImportanceRank.S, ImportanceRank.A], EstimateTo = 120, DeadlineFrom = new(2026, 11, 20), DeadlineTo = new(2026, 11, 20) };
        Check(service.QueryTasks(settings).Single().Id == a.Id, "rank OR combines with inclusive deadline and estimate AND");
        service.SaveTaskListSettings(TaskSection.Calendar, settings);
        var reopened = new TaskService(Path.Combine(directory, "lists.db")); reopened.Initialize();
        Check(reopened.QueryTasks(reopened.ReadTaskListSettings(TaskSection.Calendar)).Single().Id == a.Id && reopened.ReadTaskListSettings(TaskSection.Tasks).Ranks.Length == 0,
            "list settings persist independently of other sections");
        service.Restore(a.Id);
        foreach (var descending in new[] { false, true })
        {
            foreach (var sort in new[] { TaskSort.Estimate, TaskSort.Deadline })
                Check(service.QueryTasks(new(Sort: sort, Descending: descending)).Last().Id == c.Id, "missing optional values remain last in both directions: " + sort);
        }
        Check(service.QueryTasks(new(EstimateMode: OptionalFieldFilter.Missing)).Single().Id == c.Id, "missing estimate is distinct from zero");
        try { service.SaveTaskListSettings(TaskSection.Tasks, new(EstimateFrom: 121, EstimateTo: 120)); throw new Exception("invalid range accepted"); } catch (ArgumentException) { }
        Check(service.ReadTaskListSettings(TaskSection.Tasks).EstimateFrom == null, "invalid range leaves saved settings untouched");
        foreach (var section in Enum.GetValues<TaskSection>()) service.SaveTaskListSettings(section, new(Sort: TaskSort.Created, Descending: section == TaskSection.History));
        Check(Enum.GetValues<TaskSection>().All(section => reopened.ReadTaskListSettings(section).Sort == TaskSort.Created), "all four section contracts survive reopening");
        service.SaveSettings(new(false, 30), new(true, 180, 3, new(2026, 11, 20), new(10, 0)));
        Check(service.PollSummary().Single().Subjects.Select(x => x.Key.Id).SequenceEqual(new[] { a.Id, b.Id, c.Id }), "summary ignores all screen filters and manual orders");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(directory, "lists.db")}"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TRIGGER fail_list BEFORE UPDATE ON task_list_settings BEGIN SELECT RAISE(ABORT,'blocked'); END"; command.ExecuteNonQuery();
        }
        try { service.SaveTaskListSettings(TaskSection.Tasks, new(Sort: TaskSort.Rank)); throw new Exception("failed write accepted"); } catch (Microsoft.Data.Sqlite.SqliteException) { }
        Check(reopened.ReadTaskListSettings(TaskSection.Tasks).Sort == TaskSort.Created, "storage failure preserves the previous applied settings");
    }
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private sealed class ListClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero); public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc; }
}
