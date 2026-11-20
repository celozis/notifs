using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using Notifs.Core;

namespace Notifs.App;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class TaskRow(TaskItem item, TaskService service, string calendarReasons = "") : Observable
{
    public TaskItem Item { get; private set; } = item;
    private TaskTiming timing = service.Timing(item);
    public ImportanceRank Rank => Item.Rank;
    public string Title => Item.Title;
    public string CalendarReasons { get; private set; } = calendarReasons;
    public string Description => Item.Description;
    public bool IsCompleted => Item.CompletedAt is not null;
    public bool IsIncomplete => !IsCompleted;
    public string StateLabel => IsCompleted ? "Выполнена" : "В работе";
    public string ToggleLabel => IsCompleted ? "В работу" : "Готово";
    public string CheckLabel => IsCompleted ? "✓" : "";
    public string CreatedLabel => timing.LocalCreatedAt.ToString("d MMMM yyyy, HH:mm");
    public string DeadlineLabel => Item.Deadline is { } deadline ? deadline.Date.ToString("d MMMM yyyy") + (deadline.Time is { } exact ? ", " + exact.ToString("HH:mm") : " · до конца дня") : "Без дедлайна";
    public string EstimateLabel => Item.EstimateMinutes is { } minutes ? Duration(TimeSpan.FromMinutes(minutes)) : "Без оценки";
    public string SlackLabel => timing.Slack is { } slack ? Duration(slack) : "Без дедлайна";
    public bool IsOverdue => timing.IsOverdue;
    public bool HasNegativeSlack => IsIncomplete && !IsOverdue && timing.Slack < TimeSpan.Zero;
    public string UrgencyLabel => IsOverdue ? "Просрочена" : HasNegativeSlack ? "Оценка превышает время до дедлайна" : "";
    public string MetadataLabel => (Item.Deadline is { } deadline ? deadline.Date.ToString("d MMMM") + (deadline.Time is { } exact ? ", " + exact.ToString("HH:mm") : " · весь день") : "Без дедлайна") + " · " + EstimateLabel;
    public string AgeLabel
    {
        get
        {
            var days = Math.Max(0, (timing.LocalNow.Date - timing.LocalCreatedAt.Date).Days);
            return days switch { 0 => "Создана сегодня", 1 => "Создана вчера", _ => $"Создана {days} дн. назад" };
        }
    }
    public void Update(TaskItem value, string? reasons = null)
    {
        if (reasons != null) { CalendarReasons = reasons; Changed(nameof(CalendarReasons)); }
        Item = value;
        timing = service.Timing(value);
        foreach (var name in new[] { nameof(Item), nameof(Title), nameof(Description), nameof(Rank), nameof(IsCompleted), nameof(IsIncomplete), nameof(StateLabel), nameof(ToggleLabel), nameof(CheckLabel), nameof(CreatedLabel), nameof(AgeLabel), nameof(DeadlineLabel), nameof(EstimateLabel), nameof(SlackLabel), nameof(IsOverdue), nameof(HasNegativeSlack), nameof(UrgencyLabel), nameof(MetadataLabel) }) Changed(name);
    }
    public static string Duration(TimeSpan value)
    {
        var negative = value < TimeSpan.Zero;
        var span = value.Duration();
        var hours = (long)span.TotalHours;
        var text = hours > 0 ? $"{hours} ч" + (span.Minutes > 0 ? $" {span.Minutes} мин" : "") : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} мин" : span == TimeSpan.Zero ? "0 мин" : "< 1 мин";
        return (negative ? "−" : "") + text;
    }
}

public enum TaskFilter { All, Incomplete, Completed }

public sealed partial class MainViewModel : Observable
{
    private readonly TaskService service;
    internal TaskService Service => service;
    public bool HasDeleted { get; private set; }
    public AsyncCommand RestoreDeletedCommand { get; }
    public event Action<EntityKey>? DeletedRestored;
    private List<TaskItem> all = [];
    private TaskRow? selected;
    private bool history;
    private bool busy;
    private bool initialized;
    private string status = "Открываем ваши задачи…";
    private string error = "";
    private TaskFilter filter;
    private readonly Dictionary<Guid, int> heldPositions = [];
    public ObservableCollection<TaskRow> Tasks { get; } = [];
    public TaskRow? Selected { get => selected; set { selected = value; Changed(); Changed(nameof(HasSelection)); CommandManager.InvalidateRequerySuggested(); } }
    public bool HasSelection => Selected is not null;
    public bool IsHistory => history;
    public bool IsActive => !history;
    public bool IsAll => filter == TaskFilter.All;
    public bool IsIncomplete => filter == TaskFilter.Incomplete;
    public bool IsCompleted => filter == TaskFilter.Completed;
    public string AllLabel => $"Все {all.Count}";
    public string IncompleteLabel => $"Не сделаны {all.Count(x => x.CompletedAt is null)}";
    public string CompletedLabel => $"Сделаны {all.Count(x => x.CompletedAt is not null)}";
    public bool IsBusy { get => busy; private set { busy = value; Changed(); CommandManager.InvalidateRequerySuggested(); } }
    public string PageTitle => calendar ? Calendar.IsMonth ? "Календарь целиком" : "Календарь" : history ? "История" : "Мои задачи";
    public string Subtitle => calendar ? Calendar.IsMonth ? "Выберите день, чтобы увидеть связанные задачи." : "Неделя рядом с задачами. Полный месяц — в один клик." : history ? "Всё, что уже получилось сделать." : "Список слева. Внимание — на одном деле.";
    public string CountLabel => $"ЗАДАЧИ · {Tasks.Count}";
    public string EmptyTitle => history ? "Здесь будут ваши результаты" : "Пока нет задач";
    public string EmptyMessage => history ? "Выполненные задачи появятся здесь. Любую можно вернуть в работу." : "Создайте задачу или выберите другой фильтр.";
    public bool IsEmpty => Tasks.Count == 0;
    public string Status { get => status; private set { status = value; Changed(); } }
    public string Error { get => error; private set { error = value; Changed(); Changed(nameof(HasError)); CommandManager.InvalidateRequerySuggested(); } }
    public bool HasError => Error.Length > 0;
    public AsyncCommand ActiveCommand { get; }
    public AsyncCommand HistoryCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand AllCommand { get; }
    public AsyncCommand IncompleteCommand { get; }
    public AsyncCommand CompletedCommand { get; }
    public AsyncCommand CompleteCommand { get; }
    public AsyncCommand RestoreCommand { get; }
    public ParameterAsyncCommand ToggleCommand { get; }

    public MainViewModel(TaskService service)
    {
        this.service = service;
        RestoreDeletedCommand = new(async () =>
        {
            IsBusy = true;
            EntityKey? restored = null;
            try { restored = await Task.Run(service.RestoreLastDeleted); }
            catch (Exception ex) when (IsStorageError(ex)) { ShowStorageError(); }
            finally { IsBusy = false; }
            if (restored is not null)
            {
                if (restored.Type == "task") { history = false; filter = TaskFilter.All; }
                await LoadAsync(restored.Type == "task" ? restored.Id : null, true);
                DeletedRestored?.Invoke(restored);
            }
        }, () => HasDeleted && !IsBusy);
        ActiveCommand = new(() => SwitchAsync(false), () => !IsBusy);
        HistoryCommand = new(() => SwitchAsync(true), () => !IsBusy);
        RefreshCommand = new(() => LoadAsync(), () => !IsBusy);
        AllCommand = new(() => SetFilterAsync(TaskFilter.All), () => !IsBusy);
        IncompleteCommand = new(() => SetFilterAsync(TaskFilter.Incomplete), () => !IsBusy);
        CompletedCommand = new(() => SetFilterAsync(TaskFilter.Completed), () => !IsBusy);
        CompleteCommand = new(() => ChangeStateAsync(Selected!, false), () => Selected is { IsCompleted: false } && !IsBusy && !HasError);
        RestoreCommand = new(() => ChangeStateAsync(Selected!, true), () => Selected is { IsCompleted: true } && !IsBusy && !HasError);
        ToggleCommand = new(value => value is TaskRow row ? ChangeStateAsync(row, row.IsCompleted) : Task.CompletedTask, () => !IsBusy && !HasError);
    }

    public async Task LoadAsync(Guid? selectId = null, bool reapplyFilter = false)
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = "";
        try
        {
            var items = await Task.Run(() =>
            {
                if (!initialized) { service.Initialize(); initialized = true; }
                HasDeleted = service.DeletedKind is not null;
                Settings = service.ReadTaskListSettings(Section);
                filter = (TaskFilter)Settings.Status;
                return service.ListAll();
            });
            var id = selectId ?? Selected?.Item.Id;
            all = service.ApplyTaskList(PeriodTasks(items), Settings with { Status = TaskStatusFilter.All }).ToList();
            if (reapplyFilter || Tasks.Count == 0)
            {
                heldPositions.Clear();
                Tasks.Clear();
                foreach (var item in all.Where(MatchesFilter)) Tasks.Add(new(item, service, calendar ? MainWindow.ReasonLabel(service.ReasonsFor(item, Calendar.Period)) : ""));
            }
            else
            {
                foreach (var row in Tasks.ToArray())
                {
                    var updated = all.FirstOrDefault(x => x.Id == row.Item.Id);
                    if (updated is null) Tasks.Remove(row); else row.Update(updated, calendar ? MainWindow.ReasonLabel(service.ReasonsFor(updated, Calendar.Period)) : "");
                }
                // Newly saved tasks are visible immediately, without moving existing cards.
                foreach (var item in all.Where(x => x.Id == selectId && Tasks.All(row => row.Item.Id != x.Id))) Tasks.Add(new(item, service));
                RefreshTime();
            }
            Selected = Tasks.FirstOrDefault(row => row.Item.Id == id) ?? Tasks.FirstOrDefault();
            Status = "Все изменения сохранены на этом компьютере";
            RefreshLabels();
            Changed(nameof(SortLabel)); Changed(nameof(IsCalendar)); CalendarChanged?.Invoke();
            Changed(nameof(HasDeleted));
        }
        catch (Exception ex) when (IsStorageError(ex)) { ShowStorageError(); }
        finally { IsBusy = false; }
    }

    private bool MatchesFilter(TaskItem item) => filter switch
    {
        TaskFilter.Incomplete => item.CompletedAt is null,
        TaskFilter.Completed => item.CompletedAt is not null,
        _ => true
    };
    private async Task SetFilterAsync(TaskFilter value) { await ApplySettingsAsync(Settings with { Status = (TaskStatusFilter)value }); }
    private async Task SwitchAsync(bool showHistory) { history = showHistory; calendar = false; await LoadAsync(reapplyFilter: true); }

    private async Task ChangeStateAsync(TaskRow row, bool restore)
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = "";
        try
        {
            var updated = await Task.Run(() => restore ? service.Restore(row.Item.Id) : service.Complete(row.Item.Id));
            // Update only after SQLite confirms the write; keep the row and its position.
            row.Update(updated);
            if (Tasks.Contains(row)) heldPositions.TryAdd(row.Item.Id, Tasks.IndexOf(row));
            var index = all.FindIndex(x => x.Id == updated.Id);
            if (index >= 0) all[index] = updated;
            calendarSource = calendarSource.Select(item => item.Id == updated.Id ? updated : item).ToArray();
            CalendarChanged?.Invoke();
            RefreshLabels();
            Status = restore ? "Задача возвращена в работу" : "Задача выполнена";
        }
        catch (Exception ex) when (IsStorageError(ex)) { ShowStorageError(); }
        finally { IsBusy = false; }
    }

    public EditorViewModel CreateEditor(bool edit) => new(service, edit ? Selected?.Item : null);
    public async Task CompleteFromNotificationAsync(Guid id)
    {
        if (IsBusy) throw new IOException("Дождитесь текущей операции и повторите действие.");
        var row = Tasks.FirstOrDefault(x => x.Item.Id == id) ?? new TaskRow(service.ListAll().SingleOrDefault(x => x.Id == id)
            ?? throw new KeyNotFoundException("Задача больше не существует."), service);
        await ChangeStateAsync(row, false);
        if (HasError) throw new IOException(Error);
    }
    public async Task SelectTaskAsync(Guid id)
    {
        history = false; calendar = false;
        await LoadAsync(id, true);
        // Opening a concrete notification must remain possible even when the saved list excludes it.
        if (!HasError && Tasks.All(row => row.Item.Id != id))
        {
            var task = service.ListAll().SingleOrDefault(item => item.Id == id);
            if (task != null) { var row = new TaskRow(task, service); Tasks.Add(row); Selected = row; RefreshLabels(); }
        }
    }

    public async Task OpenTaskListAsync()
    {
        history = false;
        calendar = false;
        await LoadAsync(reapplyFilter: true);
    }
    private const string NotificationError = "Не удалось обработать напоминания. Проверьте доступ к папке данных; повторим автоматически.";
    internal void ReportNotificationError() { if (!HasError || Error == NotificationError) Error = NotificationError; }
    internal void ClearNotificationError() { if (Error == NotificationError) Error = ""; }
    public async Task<bool> DeleteAsync(Guid id)
    {
        IsBusy = true;
        try { await Task.Run(() => service.DeleteTask(id)); }
        catch (Exception ex) when (IsStorageError(ex)) { ShowStorageError(); return false; }
        finally { IsBusy = false; }
        await LoadAsync(reapplyFilter: true);
        Status = "Задача удалена. Последнее удаление можно восстановить.";
        return true;
    }
    public async Task EditorSavedAsync(Guid id, bool created)
    {
        if (created && !calendar) { history = false; }
        await LoadAsync(id, created);
    }
    public void RefreshTime()
    {
        if (IsBusy && all.Count == 0) return;
        foreach (var row in Tasks) row.Update(row.Item);
        var available = service.OrderTaskList(Tasks.Where(x => !heldPositions.ContainsKey(x.Item.Id)).Select(x => x.Item), Settings)
            .Select(item => Tasks.Single(row => row.Item.Id == item.Id)).ToList();
        foreach (var row in Tasks.Where(x => heldPositions.ContainsKey(x.Item.Id)).OrderBy(x => heldPositions[x.Item.Id]))
            available.Insert(Math.Min(heldPositions[row.Item.Id], available.Count), row);
        for (var i = 0; i < available.Count; i++)
            if (!ReferenceEquals(Tasks[i], available[i])) Tasks.Move(Tasks.IndexOf(available[i]), i);
    }
    private void RefreshLabels()
    {
        foreach (var property in new[] { nameof(IsHistory), nameof(IsActive), nameof(IsAll), nameof(IsIncomplete), nameof(IsCompleted), nameof(AllLabel), nameof(IncompleteLabel), nameof(CompletedLabel), nameof(PageTitle), nameof(Subtitle), nameof(CountLabel), nameof(EmptyTitle), nameof(EmptyMessage), nameof(IsEmpty) }) Changed(property);
    }
    private void ShowStorageError() { Error = "Не удалось открыть или сохранить задачи. Проверьте доступ к папке данных и нажмите «Повторить»."; Status = "Изменения не подтверждены"; }
    internal static bool IsStorageError(Exception ex) => ex is SqliteException or IOException or UnauthorizedAccessException or KeyNotFoundException;
}

public sealed class EditorViewModel : Observable
{
    private readonly TaskService service;
    private readonly TaskItem? original;
    private string title;
    private string description;
    private string error = "";
    private bool busy;
    private bool saved;
    private string deadlineDate;
    private string deadlineTime;
    private string estimateHours;
    private string estimateMinutes;
    private ImportanceRank rank;
    private PersonalSchedule? originalPersonal;
    private bool personalEnabled;
    private string personalDate = "", personalTime = "", repeatMinutes = "";
    public bool PersonalEnabled { get => personalEnabled; set { personalEnabled = value; FieldChanged(); } }
    public string PersonalDate { get => personalDate; set { personalDate = value; FieldChanged(); } }
    public string PersonalTime { get => personalTime; set { personalTime = value; FieldChanged(); } }
    public string RepeatMinutes { get => repeatMinutes; set { repeatMinutes = value; FieldChanged(); } }
    public string DeadlineDate { get => deadlineDate; set { deadlineDate = value; FieldChanged(); } }
    public string DeadlineTime { get => deadlineTime; set { deadlineTime = value; FieldChanged(); } }
    public string EstimateHours { get => estimateHours; set { estimateHours = value; FieldChanged(); } }
    public string EstimateMinutes { get => estimateMinutes; set { estimateMinutes = value; FieldChanged(); } }
    public ImportanceRank Rank { get => rank; set { rank = value; FieldChanged(); } }
    public IReadOnlyList<ImportanceRank> Ranks { get; } = Enum.GetValues<ImportanceRank>();
    public string InvalidField { get; private set; } = "TitleInput";
    public event Action<Guid>? Saved;
    public event Action? ValidationFailed;
    public string Heading => original is null ? "Новое дело" : "Редактировать задачу";
    public bool CanDelete => original is not null;
    public TaskItem? SavedTask => original;
    public string Title { get => title; set { title = value; FieldChanged(); } }
    public string Description { get => description; set { description = value; FieldChanged(); } }
    public string Error { get => error; private set { error = value; Changed(); Changed(nameof(HasError)); } }
    public bool HasError => Error.Length > 0;
    public bool IsBusy { get => busy; private set { busy = value; Changed(); Changed(nameof(CanEdit)); Changed(nameof(SaveLabel)); CommandManager.InvalidateRequerySuggested(); } }
    public bool CanEdit => !IsBusy;
    public string SaveLabel => IsBusy ? "Сохраняем…" : "Сохранить задачу";
    public bool IsDirty => !saved && (Title != (original?.Title ?? "") || Description != (original?.Description ?? "")
        || DeadlineDate != (original?.Deadline?.Date.ToString("dd.MM.yyyy") ?? "") || DeadlineTime != (original?.Deadline?.Time?.ToString("HH:mm") ?? "")
        || EstimateHours != OriginalHours || EstimateMinutes != OriginalMinutes || Rank != (original?.Rank ?? ImportanceRank.B)
        || PersonalEnabled != (originalPersonal is not null) || PersonalDate != (originalPersonal?.Date.ToString("dd.MM.yyyy") ?? "")
        || PersonalTime != (originalPersonal?.Time.ToString("HH:mm") ?? "") || RepeatMinutes != (originalPersonal?.RepeatMinutes?.ToString() ?? ""));
    private string OriginalHours => original?.EstimateMinutes is { } minutes ? (minutes / 60).ToString() : "";
    private string OriginalMinutes => original?.EstimateMinutes is { } minutes ? (minutes % 60).ToString() : "";
    public AsyncCommand SaveCommand { get; }
    public RelayCommand DiscardCommand { get; }
    public EditorViewModel(TaskService service, TaskItem? original)
    {
        this.service = service;
        this.original = original;
        title = original?.Title ?? "";
        description = original?.Description ?? "";
        deadlineDate = original?.Deadline?.Date.ToString("dd.MM.yyyy") ?? "";
        deadlineTime = original?.Deadline?.Time?.ToString("HH:mm") ?? "";
        estimateHours = OriginalHours;
        estimateMinutes = OriginalMinutes;
        rank = original?.Rank ?? ImportanceRank.B;
        try
        {
            originalPersonal = original is null ? null : service.ReadPersonal(original.Id)?.Schedule;
            personalEnabled = originalPersonal is not null;
            personalDate = originalPersonal?.Date.ToString("dd.MM.yyyy") ?? "";
            personalTime = originalPersonal?.Time.ToString("HH:mm") ?? "";
            repeatMinutes = originalPersonal?.RepeatMinutes?.ToString() ?? "";
            if (service.ReadDraft(original?.Id) is { } draft) ApplyDraft(draft);
        }
        catch (Exception ex) when (MainViewModel.IsStorageError(ex)) { Error = "Не удалось прочитать черновик. Проверьте папку данных."; }
        SaveCommand = new(SaveAsync, () => !IsBusy && !saved);
        DiscardCommand = new(() =>
        {
            try
            {
                service.DiscardDraft(original?.Id);
                ApplyDraft(new(original?.Title ?? "", original?.Description ?? "", original?.Deadline?.Date.ToString("dd.MM.yyyy") ?? "", original?.Deadline?.Time?.ToString("HH:mm") ?? "", OriginalHours, OriginalMinutes, original?.Rank ?? ImportanceRank.B,
                    originalPersonal is not null, originalPersonal?.Date.ToString("dd.MM.yyyy") ?? "", originalPersonal?.Time.ToString("HH:mm") ?? "", originalPersonal?.RepeatMinutes?.ToString() ?? ""));
                Error = "";
                foreach (var name in new[] { nameof(Title), nameof(Description), nameof(DeadlineDate), nameof(DeadlineTime), nameof(EstimateHours), nameof(EstimateMinutes), nameof(Rank), nameof(PersonalEnabled), nameof(PersonalDate), nameof(PersonalTime), nameof(RepeatMinutes), nameof(IsDirty) }) Changed(name);
            }
            catch (Exception ex) when (MainViewModel.IsStorageError(ex)) { Error = "Не удалось отбросить изменения. Черновик остаётся в форме."; }
        }, () => !IsBusy && !saved);
    }
    private void ApplyDraft(TaskDraft draft)
    {
        title = draft.Title; description = draft.Description; deadlineDate = draft.DeadlineDate; deadlineTime = draft.DeadlineTime;
        estimateHours = draft.EstimateHours; estimateMinutes = draft.EstimateMinutes; rank = draft.Rank;
        personalEnabled = draft.PersonalEnabled; personalDate = draft.PersonalDate; personalTime = draft.PersonalTime; repeatMinutes = draft.RepeatMinutes;
    }
    public bool PersistDraft()
    {
        if (saved) return true;
        try
        {
            if (!IsDirty) service.DiscardDraft(original?.Id);
            else service.SaveDraft(original?.Id, new(Title, Description, DeadlineDate, DeadlineTime, EstimateHours, EstimateMinutes, Rank, PersonalEnabled, PersonalDate, PersonalTime, RepeatMinutes));
            return true;
        }
        catch (Exception ex) when (MainViewModel.IsStorageError(ex)) { Error = "Не удалось записать черновик. Ввод остаётся здесь; повторите сохранение перед выходом."; return false; }
    }
    private async Task SaveAsync()
    {
        Error = "";
        if (string.IsNullOrWhiteSpace(Title)) { Invalid("TitleInput", "Введите название задачи. Остальные поля останутся на месте."); return; }
        if (!TryFields(out var deadline, out var estimate)) return;
        if (!TryPersonal(out var personal)) return;
        var submittedTitle = Title;
        var submittedDescription = Description;
        IsBusy = true;
        try
        {
            var id = await Task.Run(() =>
            {
                return service.SaveEditor(original?.Id, submittedTitle, submittedDescription, deadline, estimate, Rank, personal).Id;
            });
            saved = true;
            Saved?.Invoke(id);
        }
        catch (Exception ex) when (MainViewModel.IsStorageError(ex)) { Error = "Не удалось сохранить. Проверьте доступ к папке данных и попробуйте ещё раз. Ваш текст остаётся в форме."; }
        catch (ArgumentException ex)
        {
            IsBusy = false;
            Invalid(deadline?.Time is null ? "DeadlineDateInput" : "DeadlineTimeInput", ex.Message);
        }
        finally { IsBusy = false; }
    }
    private void Invalid(string field, string message) { InvalidField = field; Error = message; ValidationFailed?.Invoke(); }
    private void FieldChanged([CallerMemberName] string? name = null) { Changed(name); Changed(nameof(IsDirty)); Error = ""; PersistDraft(); }
    private bool TryFields(out TaskDeadline? deadline, out int? estimate)
    {
        deadline = null; estimate = null;
        if (string.IsNullOrWhiteSpace(DeadlineDate) && !string.IsNullOrWhiteSpace(DeadlineTime))
        { Invalid("DeadlineDateInput", "Добавьте дату дедлайна или очистите время."); return false; }
        if (!string.IsNullOrWhiteSpace(DeadlineDate))
        {
            if (!DateOnly.TryParseExact(DeadlineDate.Trim(), "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
            { Invalid("DeadlineDateInput", "Введите существующую дату в формате ДД.ММ.ГГГГ или очистите поле."); return false; }
            TimeOnly? exact = null;
            if (!string.IsNullOrWhiteSpace(DeadlineTime))
            {
                if (!TimeOnly.TryParseExact(DeadlineTime.Trim(), "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
                { Invalid("DeadlineTimeInput", "Введите время от 00:00 до 23:59 или очистите поле."); return false; }
                exact = parsed;
            }
            deadline = new(date, exact);
        }
        if (!string.IsNullOrWhiteSpace(EstimateHours) || !string.IsNullOrWhiteSpace(EstimateMinutes))
        {
            if (!WholeNumber(EstimateHours, out var hours) || !WholeNumber(EstimateMinutes, out var minutes)
                || (long)hours * 60 + minutes is <= 0 or > int.MaxValue)
            { Invalid("EstimateHoursInput", "Введите положительную полную оценку целыми часами и минутами. Очистите оба поля, чтобы снять оценку."); return false; }
            estimate = hours * 60 + minutes;
        }
        return true;
    }
    private static bool WholeNumber(string text, out int value)
    {
        value = 0;
        return string.IsNullOrWhiteSpace(text) || int.TryParse(text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);
    }
    private bool TryPersonal(out PersonalSchedule? schedule)
    {
        schedule = null;
        if (!PersonalEnabled) return true;
        if (!DateOnly.TryParseExact(PersonalDate.Trim(), "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
        { Invalid("PersonalDateInput", "Укажите дату первого напоминания: ДД.ММ.ГГГГ."); return false; }
        if (!TimeOnly.TryParseExact(PersonalTime.Trim(), "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var exact))
        { Invalid("PersonalTimeInput", "Укажите местное время первого напоминания: ЧЧ:ММ."); return false; }
        int? repeat = null;
        if (!string.IsNullOrWhiteSpace(RepeatMinutes))
        {
            if (!WholeNumber(RepeatMinutes, out var minutes) || minutes <= 0)
            { Invalid("RepeatMinutesInput", "Повтор: положительное целое число минут либо пустое поле."); return false; }
            repeat = minutes;
        }
        schedule = new(date, exact, repeat);
        return true;
    }
}
