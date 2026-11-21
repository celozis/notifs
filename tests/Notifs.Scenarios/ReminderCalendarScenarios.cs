using Notifs.Core;

namespace Notifs.Scenarios;

internal static class ReminderCalendarScenarios
{
    public static void Run(string directory)
    {
        var clock = new SessionScenarios.Clock(new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var service = new TaskService(System.IO.Path.Combine(directory, "reminder-calendar.db"), clock); service.Initialize();
        var rule = new SignalSchedule(new(2026, 11, 16), new(12, 0), SignalRepeat.AfterCompletion, [], new(12, 0), 0, 14, true);
        var reminder = service.SaveSignal(null, "Сменить наволочку", "", rule, true, true);
        service.PollSignals(); clock.Advance(TimeSpan.FromDays(1));
        var occurrence = service.ListSignals().Single().Current!; service.CompleteRecurring(occurrence.Id);
        var period = new CalendarPeriod(new(2026, 11, 16), new(2026, 12, 31));
        var entries = service.QueryCalendarEntries(period, new());
        DraftScenarios.Check(entries.Count == 2 && entries.Count(x => x.Reminder?.Kind == ReminderCalendarKind.Completed) == 1, "calendar keeps one completion with both bases and only one known interval future");
        DraftScenarios.Check(service.QueryCalendarEntries(period, new() { Ranks = [ImportanceRank.S] }).Count == 0, "ordinary rank filters exclude recurring entries without invented ranks");
        service.SetSignalEnabled(reminder.Id, false);
        DraftScenarios.Check(service.QueryCalendarEntries(period, new()).Count == 1, "disabled schedule retains history and removes future planned dates");
        service.DeleteSignal(reminder.Id);
        DraftScenarios.Check(service.QueryCalendarEntries(period, new()).Count == 0, "deletion removes all recurring calendar facts");
        service.RestoreLastDeleted();
        DraftScenarios.Check(service.QueryCalendarEntries(period, new()).Single().Reminder?.Kind == ReminderCalendarKind.Completed, "restoration returns history while schedule remains disabled");
    }
}
