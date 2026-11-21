using Notifs.Core;

namespace Notifs.App;

public sealed partial class MainViewModel
{
    private bool calendar;
    public TaskSection Section => calendar ? TaskSection.Calendar : history ? TaskSection.History : inProgress ? TaskSection.InProgress : TaskSection.Tasks;
    public TaskListSettings Settings { get; private set; } = new();
    public bool IsCalendar => calendar;
    public string SortLabel => new[] { "По срочности", "По рангу", "По полной оценке", "По дедлайну", "По созданию" }[(int)Settings.Sort] +
        (Settings.Sort == TaskSort.Urgency ? "" : Settings.Descending ? " ↓" : " ↑") + (Settings.HasFieldFilters ? " · Есть отбор" : "") +
        (Settings.Status == TaskStatusFilter.All ? "" : Settings.Status == TaskStatusFilter.Incomplete ? " · Не сделаны" : " · Сделаны");
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
    private Dictionary<Guid, CalendarReason> calendarReasons = [];
    private string CalendarReasonsFor(TaskItem item) => TaskService.CalendarReasonText(calendarReasons.GetValueOrDefault(item.Id));
    public event Action? CalendarChanged;
    public async Task OpenCalendarAsync()
    {
        try { Calendar = service.ReadCalendarState(); }
        catch (Exception ex) when (IsStorageError(ex)) { ShowStorageError(); return; }
        calendar = true; history = false; inProgress = false;
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
        if (calendar && Settings.CalendarType == CalendarItemType.Recurring) return [];
        if (inProgress) { var started = service.ReadSessions().Where(x => !x.Finished).Select(x => x.TaskId).ToHashSet(); return calendarSource.Where(x => x.CompletedAt == null && started.Contains(x.Id)); }
        if (!calendar) return calendarSource;
        var projected = service.ProjectCalendar(calendarSource, Calendar.Period);
        calendarReasons = projected.ToDictionary(x => x.Task.Id, x => x.Reasons);
        return projected.Select(x => x.Task);
    }
}
