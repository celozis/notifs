using System.IO;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static class CalendarScenarios
{
    public static void Run(string directory)
    {
        var clock = new CalendarClock(); var path = Path.Combine(directory, "calendar.db");
        var service = new TaskService(path, clock); service.Initialize();
        var span = service.Create("Суббота +72 часа", "", estimateMinutes: 4320);
        var monday = new CalendarPeriod(new(2026, 11, 16), new(2026, 11, 16));
        Check(service.QueryCalendar(monday, new()).Single().Reasons == CalendarReason.EstimatedSpan, "Saturday plus 72 hours includes Monday with estimated reason");
        var same = service.Create("Создана и срок", "", new(new(2026, 11, 14)), 60); service.Complete(same.Id);
        var saturday = service.QueryCalendar(new(new(2026, 11, 14), new(2026, 11, 14)), new());
        Check(saturday.Single(x => x.Task.Id == same.Id).Reasons == (CalendarReason.Created | CalendarReason.Deadline), "one completed row includes both creation and deadline, without estimated span");
        Check(service.QueryCalendar(new(new(2026, 11, 9), new(2026, 11, 15)), new()).Any(x => x.Task.Id == span.Id) && service.QueryCalendar(new(new(2026, 11, 16), new(2026, 11, 22)), new()).Any(x => x.Task.Id == span.Id), "estimated span crosses both weeks");
        var state = new CalendarState(new(2026, 12, 1), monday.Start, false, true);
        service.SaveCalendarState(state);
        var reopened = new TaskService(path, clock); reopened.Initialize();
        Check(reopened.ReadCalendarState() == state, "calendar month, selected day and mode survive restart");
        Check(service.Timing(span).DeadlineAt == null && !service.Timing(span).IsOverdue, "estimated span never becomes a deadline or overdue");
        clock.Now = new(2026, 12, 31, 19, 0, 0, TimeSpan.Zero);
        var midnight = service.Create("Местный Новый год", "", new(new(2027, 1, 1)), 1);
        Check(service.QueryCalendar(new(new(2027, 1, 1), new(2027, 1, 1)), new()).Single().Task.Id == midnight.Id &&
            service.QueryCalendar(new(new(2026, 12, 31), new(2026, 12, 31)), new()).Count == 0, "calendar uses local midnight across the year, including date-only deadlines");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TRIGGER fail_calendar BEFORE UPDATE ON calendar_state BEGIN SELECT RAISE(ABORT,'blocked'); END"; command.ExecuteNonQuery();
        }
        try { service.SaveCalendarState(state with { IsMonth = false }); throw new Exception("failed write accepted"); } catch (Microsoft.Data.Sqlite.SqliteException) { }
        Check(reopened.ReadCalendarState() == state, "failed calendar write preserves the previous mode and period");
    }
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); Console.WriteLine("PASS " + message); }
    internal sealed class CalendarClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 14, 7, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Calendar UTC+5", TimeSpan.FromHours(5), "Test", "Test");
    }
}
