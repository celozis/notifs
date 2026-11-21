namespace Notifs.Core;

public sealed record CalendarEntry(TaskItem? Task, ReminderCalendarEntry? Reminder, string Reasons)
{
    public string Title => Task?.Title ?? Reminder!.Title;
    public bool IsCompleted => Task?.CompletedAt != null || Reminder?.Kind == ReminderCalendarKind.Completed;
}

public sealed partial class TaskService
{
    public IReadOnlyList<CalendarEntry> QueryCalendarEntries(CalendarPeriod period, TaskListSettings settings)
    {
        var entries = new List<CalendarEntry>();
        if (settings.CalendarType != CalendarItemType.Recurring)
            entries.AddRange(QueryCalendar(period, settings).Select(x => new CalendarEntry(x.Task, null, CalendarReasonText(x.Reasons))));
        if (settings.CalendarType == CalendarItemType.Tasks || settings.HasFieldFilters) return entries;
        var from = new TaskDeadline(period.Start, TimeOnly.MinValue).Resolve(time.LocalTimeZone);
        var until = new TaskDeadline(period.End.AddDays(1), TimeOnly.MinValue).Resolve(time.LocalTimeZone);
        foreach (var reminder in ListReminderCalendar(from, until))
        {
            var completed = reminder.Kind == ReminderCalendarKind.Completed;
            if (settings.Status != TaskStatusFilter.All && completed != (settings.Status == TaskStatusFilter.Completed)) continue;
            var reasons = new List<string>();
            if (reminder.Kind == ReminderCalendarKind.Planned) reasons.Add("Плановое повторение");
            else if (period.Contains(LocalDate(reminder.DueAt))) reasons.Add("Исходный срок");
            if (reminder.CompletedAt is { } done && period.Contains(LocalDate(done))) reasons.Add("Фактическое выполнение");
            entries.Add(new(null, reminder, string.Join(" · ", reasons)));
        }
        return entries;
    }
    public static string CalendarReasonText(CalendarReason reasons) => string.Join(" · ", new[]
    {
        (CalendarReason.Created, "Создана"), (CalendarReason.Deadline, "Дедлайн"), (CalendarReason.EstimatedSpan, "Расчётный промежуток"),
        (CalendarReason.Started, "Начало занятия"), (CalendarReason.CheckIn, "Вопрос о результате")
    }.Where(x => reasons.HasFlag(x.Item1)).Select(x => x.Item2));
}
