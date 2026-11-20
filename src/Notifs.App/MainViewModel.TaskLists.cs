using Notifs.Core;

namespace Notifs.App;

public sealed partial class MainViewModel
{
    private bool calendar;
    public TaskSection Section => calendar ? TaskSection.Calendar : history ? TaskSection.History : TaskSection.Tasks;
    public TaskListSettings Settings { get; private set; } = new();
    public bool IsCalendar => calendar;
    public string SortLabel => new[] { "По срочности", "По рангу", "По полной оценке", "По дедлайну", "По созданию" }[(int)Settings.Sort] +
        (Settings.Sort == TaskSort.Urgency ? "" : Settings.Descending ? " ↓" : " ↑") + (Settings.HasFieldFilters ? " · Есть отбор" : "");
    public async Task<bool> ApplySettingsAsync(TaskListSettings value)
    {
        IsBusy = true;
        try { await Task.Run(() => service.SaveTaskListSettings(Section, value)); }
        catch (Exception ex) when (IsStorageError(ex)) { ShowStorageError(); return false; }
        finally { IsBusy = false; }
        await LoadAsync(reapplyFilter: true); Changed(nameof(SortLabel)); return !HasError;
    }
    public CalendarState Calendar { get; private set; } = new(new(2026, 1, 1), new(2026, 1, 1));
    private IReadOnlyList<TaskItem> calendarSource = [];
    public event Action? CalendarChanged;
    public async Task OpenCalendarAsync()
    {
        try { Calendar = service.ReadCalendarState(); }
        catch (Exception ex) when (IsStorageError(ex)) { ShowStorageError(); return; }
        calendar = true; history = false;
        await LoadAsync(reapplyFilter: true);
    }
    public async Task<bool> ChangeCalendarAsync(CalendarState state)
    {
        IsBusy = true;
        try { await Task.Run(() => service.SaveCalendarState(state)); }
        catch (Exception ex) when (IsStorageError(ex)) { ShowStorageError(); return false; }
        finally { IsBusy = false; }
        Calendar = state; await LoadAsync(reapplyFilter: true); return !HasError;
    }
    public IReadOnlyList<CalendarTask> CalendarDay(DateOnly day) => service.ApplyTaskList(calendarSource, Settings)
        .Select(item => new CalendarTask(item, service.ReasonsFor(item, new(day, day)))).Where(x => x.Reasons != CalendarReason.None).ToArray();
    private IEnumerable<TaskItem> PeriodTasks(IEnumerable<TaskItem> items)
    {
        calendarSource = items.ToArray();
        return calendar ? calendarSource.Where(item => service.ReasonsFor(item, Calendar.Period) != CalendarReason.None) : calendarSource;
    }
}
