using System.Collections.ObjectModel;
using System.Globalization;
using Notifs.Core;

namespace Notifs.App;

public sealed class ReminderDay(DayOfWeek day, string label) : Observable
{
    public DayOfWeek Day { get; } = day;
    public string Label { get; } = label;
    private bool selected;
    public bool Selected { get => selected; set { selected = value; Changed(); } }
}

public sealed class SignalRow(SignalReminder item, TaskService service) : Observable
{
    public SignalReminder Item { get; private set; } = item;
    public void Update(SignalReminder value)
    {
        Item = value;
        foreach (var property in new[] { nameof(Item), nameof(Title), nameof(Status), nameof(ToggleLabel), nameof(ScheduleLabel), nameof(DueLabel), nameof(LastCompletion), nameof(HasHistory), nameof(HasCurrent), nameof(CanSnooze), nameof(HasDue), nameof(AgeLabel) }) Changed(property);
    }
    private static readonly string[] Names = ["вс", "пн", "вт", "ср", "чт", "пт", "сб"];
    public string Title => Item.Title;
    public string Status => (Item.Enabled ? "ВКЛЮЧЕНО" : "ВЫКЛЮЧЕНО") + (Item.RequiresCompletion ? " · С ПОДТВЕРЖДЕНИЕМ" : " · ПРОСТОЙ СИГНАЛ");
    public bool HasCurrent => Item.Current is not null;
    public bool CanSnooze => HasCurrent && Item.Enabled;
    public bool HasDue => Item.RequiresCompletion;
    public bool HasHistory => Item.History.Count > 0;
    public string LastCompletion => Item.History.LastOrDefault() is { } last ? $"Последнее выполнение: {last.CompletedAt.ToLocalTime():dd.MM.yyyy HH:mm}" : "";
    public string AgeLabel
    {
        get
        {
            if (Item.Current is not { } debt) return "";
            var age = service.RecurringAge(debt);
            return age.TotalDays >= 1 ? $"Не выполнено: {(int)age.TotalDays} дн. {age.Hours} ч." :
                age.TotalHours >= 1 ? $"Не выполнено: {(int)age.TotalHours} ч. {age.Minutes} мин." : $"Не выполнено: {Math.Max(0, (int)age.TotalMinutes)} мин.";
        }
    }
    public string DueLabel => Item.Current is { } debt ? $"Не выполнено · исходный срок: {debt.DueAt.ToLocalTime():dd.MM.yyyy HH:mm}" +
        (Item.SnoozedUntil is { } snooze ? $" · отложено до {snooze.ToLocalTime():dd.MM.yyyy HH:mm}" : "") :
        Item.NextAt is { } next ? $"Ближайший срок: {next.ToLocalTime():dd.MM.yyyy HH:mm}" : "Ближайший срок не назначен";
    public string ToggleLabel => Item.Enabled ? "Выключить" : "Включить";
    public string ScheduleLabel => (Item.RequiresCompletion ? "Дело с подтверждением · " : "Простой сигнал · ") + (Item.Schedule.Repeat switch
    {
        SignalRepeat.Hourly => $"каждый час, в :{Item.Schedule.HourlyMinute:00}",
        SignalRepeat.AfterCompletion => $"через {Item.Schedule.IntervalValue} {(Item.Schedule.IntervalDays ? "дн." : "ч.")} после выполнения",
        _ => $"{string.Join(", ", Item.Schedule.Days.OrderBy(x => ((int)x + 6) % 7).Select(x => Names[(int)x]))}, {Item.Schedule.WeeklyTime:HH:mm}"
    });
}

public sealed class RemindersViewModel : Observable
{
    private readonly TaskService service;
    private Guid? editingId;
    private string title = "", description = "", firstDate = "", firstTime = "", weeklyTime = "15:00", minute = "0", message = "";
    private bool editing, enabled = true, busy, requiresCompletion, modeConfirmation;
    private int repeatIndex;
    private string intervalValue = "14", repeatMinutes = "60";
    public string IntervalValue { get => intervalValue; set { intervalValue = value; Changed(); } }
    public string RepeatMinutes { get => repeatMinutes; set { repeatMinutes = value; Changed(); } }
    public int IntervalUnitIndex { get; set; }
    public int ModeIndex { get => requiresCompletion ? 1 : 0; set { requiresCompletion = value == 1; if (!requiresCompletion && AfterCompletion) RepeatIndex = 0; Changed(); Changed(nameof(RequiresCompletion)); Changed(nameof(IsSignal)); Changed(nameof(ScheduleHelp)); } }
    public bool RequiresCompletion => requiresCompletion;
    public bool IsSignal => !RequiresCompletion;
    public bool HasModeConfirmation { get => modeConfirmation; private set { modeConfirmation = value; Changed(); Changed(nameof(CanEdit)); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public string ScheduleHelp => AfterCompletion ? "Следующий срок отсчитывается от фактического выполнения." : RequiresCompletion ?
        "Фиксированный график не сдвигается выполнением. Пропуски сохраняются одним текущим делом." :
        "Первое срабатывание ограничивает начало расписания. После выполнения недоступно: простой сигнал не требует отметки «Готово».";
    public ObservableCollection<SignalRow> Signals { get; } = [];
    public ReminderDay[] Days { get; } = [new(DayOfWeek.Monday, "Пн"), new(DayOfWeek.Tuesday, "Вт"), new(DayOfWeek.Wednesday, "Ср"), new(DayOfWeek.Thursday, "Чт"), new(DayOfWeek.Friday, "Пт"), new(DayOfWeek.Saturday, "Сб"), new(DayOfWeek.Sunday, "Вс")];
    public string Title { get => title; set { title = value; Changed(); } }
    public string Description { get => description; set { description = value; Changed(); } }
    public string FirstDate { get => firstDate; set { firstDate = value; Changed(); } }
    public string FirstTime { get => firstTime; set { firstTime = value; Changed(); } }
    public string WeeklyTime { get => weeklyTime; set { weeklyTime = value; Changed(); } }
    public string Minute { get => minute; set { minute = value; Changed(); } }
    public bool Enabled { get => enabled; set { enabled = value; Changed(); } }
    public bool Hourly => RepeatIndex == 1;
    public bool Weekly => RepeatIndex == 0;
    public bool AfterCompletion => RepeatIndex == 2;
    public int RepeatIndex { get => repeatIndex; set { repeatIndex = value; Changed(); Changed(nameof(Hourly)); Changed(nameof(Weekly)); Changed(nameof(AfterCompletion)); Changed(nameof(ScheduleHelp)); } }
    public bool IsEditing { get => editing; private set { editing = value; Changed(); Changed(nameof(IsList)); Changed(nameof(Heading)); Changed(nameof(Subtitle)); } }
    public bool IsList => !IsEditing;
    public string Subtitle => IsEditing ? "Расписание остаётся под твоим контролем." : "Повторяющиеся сигналы и дела — в одном месте.";
    public string Heading => IsEditing ? editingId is null ? "Новое напоминание" : "Изменить напоминание" : "Напоминания";
    public bool IsBusy { get => busy; private set { busy = value; Changed(); Changed(nameof(CanEdit)); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public bool CanEdit => !IsBusy && !HasModeConfirmation;
    public bool IsEmpty => Signals.Count == 0;
    public string CountLabel => $"РАСПИСАНИЯ · {Signals.Count}";
    public string Message { get => message; private set { message = value; Changed(); Changed(nameof(HasMessage)); } }
    public bool HasMessage => Message.Length > 0;
    public bool CanRetry { get; private set; }
    private bool hasError;
    public bool HasError { get => hasError; private set { hasError = value; Changed(); } }
    public string InvalidField { get; private set; } = "TitleInput";
    public event Action? FocusEditor;
    public event Action? FocusList;
    public event Action<Guid>? ToggleSaved;
    public event Action? ValidationFailed;
    public event Action? ChangedStorage;
    public event Action<SignalReminder>? DeleteRequested;
    public event Action? ConfirmModeRequested;
    public event Action? ConfirmModeClosed;
    public AsyncCommand ConfirmModeCommand { get; }
    public RelayCommand CancelModeCommand { get; }
    public RelayCommand NewCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public ParameterAsyncCommand EditCommand { get; }
    public ParameterAsyncCommand ToggleCommand { get; }
    public ParameterAsyncCommand DeleteCommand { get; }
    public ParameterAsyncCommand CompleteCommand { get; }
    public ParameterAsyncCommand SnoozeCommand { get; }

    public RemindersViewModel(TaskService service)
    {
        this.service = service;
        NewCommand = new(() => Edit(null), () => CanEdit);
        CancelCommand = new(() => { IsEditing = false; Message = ""; }, () => CanEdit);
        RefreshCommand = new(LoadAsync, () => !IsBusy);
        SaveCommand = new(() => SaveAsync(), () => CanEdit && IsEditing);
        ConfirmModeCommand = new(async () => { await SaveAsync(true); if (!IsEditing) { HasModeConfirmation = false; ConfirmModeClosed?.Invoke(); } }, () => HasModeConfirmation && !IsBusy);
        CancelModeCommand = new(() => { HasModeConfirmation = false; ConfirmModeClosed?.Invoke(); }, () => HasModeConfirmation && !IsBusy);
        EditCommand = new(value => { if (value is SignalRow row) Edit(row.Item); return Task.CompletedTask; }, () => !IsBusy);
        DeleteCommand = new(value => { if (value is SignalRow row) DeleteRequested?.Invoke(row.Item); return Task.CompletedTask; }, () => !IsBusy);
        ToggleCommand = new(async value =>
        {
            if (value is not SignalRow row) return;
            var id = row.Item.Id;
            if (await WriteAsync(() => service.SetSignalEnabled(id, !row.Item.Enabled), "Состояние напоминания сохранено")) ToggleSaved?.Invoke(id);
        }, () => !IsBusy);
        CompleteCommand = new(async value =>
        {
            if (value is SignalRow { Item.Current: { } debt } row)
                if (await WriteAsync(() => service.CompleteRecurring(debt.Id) ?? row.Item, "Дело выполнено")) ToggleSaved?.Invoke(row.Item.Id);
        }, () => CanEdit);
        SnoozeCommand = new(async value =>
        {
            if (value is SignalRow { Item.Current: { } debt } row)
                if (await WriteAsync(() => service.SnoozeRecurring(debt.Id) ?? row.Item, "Напоминание отложено")) ToggleSaved?.Invoke(row.Item.Id);
        }, () => CanEdit);
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try { Replace(await Task.Run(service.ListSignals)); Message = ""; CanRetry = false; HasError = false; FocusList?.Invoke(); }
        catch (Exception ex) when (MainViewModel.IsStorageError(ex)) { CanRetry = true; HasError = true; Message = "Не удалось прочитать напоминания. Нажмите «Повторить»."; }
        finally { Changed(nameof(CanRetry)); IsBusy = false; }
    }

    private void Replace(IReadOnlyList<SignalReminder> items)
    {
        Signals.Clear();
        foreach (var item in items) Signals.Add(new(item, service));
        Changed(nameof(IsEmpty)); Changed(nameof(CountLabel));
    }

    public void RefreshRows()
    {
        if (IsBusy) return;
        var items = service.ListSignals();
        foreach (var row in Signals.ToArray())
        {
            if (items.FirstOrDefault(x => x.Id == row.Item.Id) is { } item) row.Update(item);
            else Signals.Remove(row);
        }
        foreach (var item in items.Where(x => Signals.All(row => row.Item.Id != x.Id))) Signals.Add(new(item, service));
        Changed(nameof(IsEmpty)); Changed(nameof(CountLabel));
    }

    private void Edit(SignalReminder? item)
    {
        editingId = item?.Id;
        var now = DateTime.Now.AddMinutes(1);
        Title = item?.Title ?? ""; Description = item?.Description ?? "";
        FirstDate = item?.Schedule.FirstDate.ToString("dd.MM.yyyy") ?? now.ToString("dd.MM.yyyy");
        FirstTime = item?.Schedule.FirstTime.ToString("HH:mm") ?? now.ToString("HH:mm");
        WeeklyTime = item?.Schedule.WeeklyTime.ToString("HH:mm") ?? "15:00";
        Minute = (item?.Schedule.HourlyMinute ?? 0).ToString(CultureInfo.InvariantCulture);
        ModeIndex = item?.RequiresCompletion == true ? 1 : 0;
        RepeatIndex = (int)(item?.Schedule.Repeat ?? SignalRepeat.Weekly);
        IntervalValue = (item?.Schedule.IntervalValue ?? 14).ToString(CultureInfo.InvariantCulture);
        IntervalUnitIndex = item?.Schedule.IntervalDays == false ? 1 : 0; Changed(nameof(IntervalUnitIndex));
        RepeatMinutes = (item?.RepeatMinutes ?? 60).ToString(CultureInfo.InvariantCulture);
        foreach (var day in Days) day.Selected = item?.Schedule.Days.Contains(day.Day) ?? day.Day == DayOfWeek.Monday;
        Enabled = item?.Enabled ?? true;
        Message = ""; HasError = false; IsEditing = true;
        FocusEditor?.Invoke();
    }

    private async Task SaveAsync(bool confirmClearDebt = false)
    {
        SignalSchedule schedule;
        var interval = 14; var repeat = 60;
        try
        {
            InvalidField = "TitleInput";
            if (string.IsNullOrWhiteSpace(Title)) throw new ArgumentException("Введите название напоминания.");
            InvalidField = "FirstDateInput";
            if (!DateOnly.TryParseExact(FirstDate.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new ArgumentException("Дата первого срабатывания: ДД.ММ.ГГГГ.");
            InvalidField = "FirstTimeInput";
            if (!TimeOnly.TryParseExact(FirstTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) throw new ArgumentException("Первое время: ЧЧ:ММ, от 00:00 до 23:59.");
            var at = TimeOnly.MinValue;
            var atMinute = 0;
            if (Hourly)
            {
                InvalidField = "MinuteInput";
                if (!int.TryParse(Minute, out atMinute) || atMinute is < 0 or > 59) throw new ArgumentException("Минута часа: целое число от 0 до 59.");
            }
            else if (Weekly)
            {
                InvalidField = "DaysInput";
                if (!Days.Any(x => x.Selected)) throw new ArgumentException("Выберите хотя бы один день недели.");
                InvalidField = "WeeklyTimeInput";
                if (!TimeOnly.TryParseExact(WeeklyTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out at)) throw new ArgumentException("Время по дням недели: ЧЧ:ММ, от 00:00 до 23:59.");
            }
            if (AfterCompletion)
            {
                InvalidField = "IntervalInput";
                if (!int.TryParse(IntervalValue, out interval) || interval is < 1 or > 36500) throw new ArgumentException("Интервал после выполнения: целое число от 1 до 36500.");
            }
            if (RequiresCompletion)
            {
                InvalidField = "RepeatMinutesInput";
                if (!int.TryParse(RepeatMinutes, out repeat) || repeat is < 1 or > 525600) throw new ArgumentException("Повтор до выполнения: от 1 до 525600 минут.");
            }
            schedule = new(date, start, (SignalRepeat)RepeatIndex, Days.Where(x => x.Selected).Select(x => x.Day).ToArray(), at, atMinute, interval, IntervalUnitIndex == 0);
        }
        catch (ArgumentException ex) { HasError = true; Message = ex.Message; ValidationFailed?.Invoke(); return; }
        var savedTitle = Title; var savedDescription = Description; var savedEnabled = Enabled;
        var confirmed = RequiresCompletion;
        if (await WriteAsync(() => service.SaveSignal(editingId, savedTitle, savedDescription, schedule, savedEnabled, confirmed, repeat, confirmClearDebt), "Напоминание сохранено")) IsEditing = false;
    }

    private async Task<bool> WriteAsync(Func<SignalReminder> write, string success)
    {
        IsBusy = true;
        try
        {
            var item = await Task.Run(write);
            var index = Signals.ToList().FindIndex(row => row.Item.Id == item.Id);
            if (index < 0) Signals.Add(new(item, service)); else Signals[index].Update(item);
            Changed(nameof(IsEmpty)); Changed(nameof(CountLabel));
            HasError = false; Message = success; ChangedStorage?.Invoke(); return true;
        }
        catch (DebtConfirmationRequiredException) { HasModeConfirmation = true; ConfirmModeRequested?.Invoke(); return false; }
        catch (ArgumentException ex) { HasError = true; Message = ex.Message; return false; }
        catch (Exception ex) when (MainViewModel.IsStorageError(ex)) { HasError = true; Message = "Не удалось сохранить напоминание. Ввод остаётся в форме; попробуйте ещё раз."; return false; }
        finally { IsBusy = false; }
    }
}
