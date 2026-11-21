using Notifs.Core;

namespace Notifs.Scenarios;

internal static class CalendarRangeScenarios
{
    public static void Run(string directory)
    {
        var clock = new SessionScenarios.Clock(new(2026, 11, 18, 7, 0, 0, TimeSpan.Zero));
        var path = System.IO.Path.Combine(directory, "range.db");
        var service = new TaskService(path, clock); service.Initialize();
        var task = service.Create("Сдать работу", "", new(new(2026, 11, 19)), 60);
        service.StartSession(task.Id);
        var period = new CalendarPeriod(new(2026, 11, 17), new(2026, 12, 8));
        var row = service.QueryCalendar(period, new()).Single();
        DraftScenarios.Check(row.Reasons.HasFlag(CalendarReason.Started) && row.Reasons.HasFlag(CalendarReason.CheckIn) && row.Reasons.HasFlag(CalendarReason.Deadline), "one calendar row combines creation, start, question and deadline across a long period");
        service.PauseSession(task.Id);
        DraftScenarios.Check(!service.QueryCalendar(period, new()).Single().Reasons.HasFlag(CalendarReason.CheckIn), "pause removes only the future check-in calendar basis");
        clock.Advance(TimeSpan.FromDays(2)); service.ResumeSession(task.Id);
        DraftScenarios.Check(service.QueryCalendar(new(new(2026, 11, 20), new(2026, 11, 20)), new()).Single().Reasons == CalendarReason.CheckIn, "resume moves question date while initial start remains unchanged");
        service.SaveCalendarState(new(new(2026, 11, 1), period.Start, RangeStart: period.End, RangeEnd: period.Start));
        DraftScenarios.Check(new TaskService(path, clock).ReadCalendarState().Period == period, "reverse calendar range normalizes and survives database reopen");
    }
}
