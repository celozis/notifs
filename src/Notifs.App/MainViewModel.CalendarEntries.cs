using System.Collections.ObjectModel;
using Notifs.Core;

namespace Notifs.App;

public sealed class CalendarReminderRow(CalendarEntry entry, TaskService service) : Observable
{
    public CalendarEntry Entry { get; } = entry;
    public string Title => Entry.Title;
    public bool IsCompleted => Entry.IsCompleted;
    public string MetadataLabel => "↻ " + (Entry.Reminder!.Kind switch { ReminderCalendarKind.Current => "Текущее дело", ReminderCalendarKind.Completed => "Выполнено", _ => "Плановое повторение" }) +
        " · " + service.LocalTime(Entry.Reminder.CompletedAt ?? Entry.Reminder.DueAt).ToString("dd.MM.yyyy HH:mm");
    public string Reasons => Entry.Reasons;
}

public sealed partial class MainViewModel
{
    public ObservableCollection<Observable> VisibleRows { get; } = [];
    private IReadOnlyList<CalendarReminderRow> calendarReminders = [];
    public Observable? SelectedEntry
    {
        get => Selected;
        set { if (value is TaskRow task) Selected = task; else if (value is CalendarReminderRow reminder) CalendarReminderOpened?.Invoke(reminder.Entry.Reminder!.ScheduleId); }
    }
    public event Action<Guid>? CalendarReminderOpened;
    private void RefreshCalendarReminders()
    {
        calendarReminders = calendar ? service.QueryCalendarEntries(Calendar.Period, Settings).Where(x => x.Reminder != null).Select(x => new CalendarReminderRow(x, service)).ToArray() : [];
        SyncVisibleRows();
    }
    private void SyncVisibleRows()
    {
        var desired = Tasks.Cast<Observable>().Concat(calendarReminders).ToArray();
        foreach (var old in VisibleRows.Where(x => !desired.Contains(x)).ToArray()) VisibleRows.Remove(old);
        for (var index = 0; index < desired.Length; index++)
        {
            var row = desired[index]; var current = VisibleRows.IndexOf(row);
            if (current < 0) VisibleRows.Insert(index, row); else if (current != index) VisibleRows.Move(current, index);
        }
        Changed(nameof(IsEmpty)); Changed(nameof(CountLabel));
    }
    public IReadOnlyList<CalendarEntry> CalendarEntries(CalendarPeriod period) => service.QueryCalendarEntries(period, Settings);
}
