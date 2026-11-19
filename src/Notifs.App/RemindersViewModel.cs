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

public sealed class SignalRow(SignalReminder item) : Observable
{
    public SignalReminder Item { get; private set; } = item;
    public void Update(SignalReminder value)
    {
        Item = value;
        foreach (var property in new[] { nameof(Item), nameof(Title), nameof(Status), nameof(ToggleLabel), nameof(ScheduleLabel) }) Changed(property);
    }
    private static readonly string[] Names = ["вс", "пн", "вт", "ср", "чт", "пт", "сб"];
    public string Title => Item.Title;
    public string Status => (Item.Enabled ? "ВКЛЮЧЕНО" : "ВЫКЛЮЧЕНО") + " · ПРОСТОЙ СИГНАЛ";
    public string ToggleLabel => Item.Enabled ? "Выключить" : "Включить";
    public string ScheduleLabel => Item.Schedule.Repeat == SignalRepeat.Hourly ? $"Простой сигнал · каждый час, в :{Item.Schedule.HourlyMinute:00}" :
        $"Простой сигнал · {string.Join(", ", Item.Schedule.Days.OrderBy(x => ((int)x + 6) % 7).Select(x => Names[(int)x]))}, {Item.Schedule.WeeklyTime:HH:mm}";
}

public sealed class RemindersViewModel : Observable
{
    private readonly TaskService service;
    private Guid? editingId;
    private string title = "", description = "", firstDate = "", firstTime = "", weeklyTime = "15:00", minute = "0", message = "";
    private bool editing, enabled = true, busy, hourly;
    public ObservableCollection<SignalRow> Signals { get; } = [];
    public ReminderDay[] Days { get; } = [new(DayOfWeek.Monday, "Пн"), new(DayOfWeek.Tuesday, "Вт"), new(DayOfWeek.Wednesday, "Ср"), new(DayOfWeek.Thursday, "Чт"), new(DayOfWeek.Friday, "Пт"), new(DayOfWeek.Saturday, "Сб"), new(DayOfWeek.Sunday, "Вс")];
    public string Title { get => title; set { title = value; Changed(); } }
    public string Description { get => description; set { description = value; Changed(); } }
    public string FirstDate { get => firstDate; set { firstDate = value; Changed(); } }
    public string FirstTime { get => firstTime; set { firstTime = value; Changed(); } }
    public string WeeklyTime { get => weeklyTime; set { weeklyTime = value; Changed(); } }
    public string Minute { get => minute; set { minute = value; Changed(); } }
    public bool Enabled { get => enabled; set { enabled = value; Changed(); } }
    public bool Hourly { get => hourly; set { hourly = value; Changed(); Changed(nameof(Weekly)); } }
    public bool Weekly => !Hourly;
    public int RepeatIndex { get => Hourly ? 1 : 0; set { Hourly = value == 1; Changed(); } }
    public bool IsEditing { get => editing; private set { editing = value; Changed(); Changed(nameof(IsList)); Changed(nameof(Heading)); } }
    public bool IsList => !IsEditing;
    public string Heading => IsEditing ? editingId is null ? "Новое напоминание" : "Изменить напоминание" : "Напоминания";
    public bool IsBusy { get => busy; private set { busy = value; Changed(); Changed(nameof(CanEdit)); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public bool CanEdit => !IsBusy;
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
    public RelayCommand NewCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public ParameterAsyncCommand EditCommand { get; }
    public ParameterAsyncCommand ToggleCommand { get; }
    public ParameterAsyncCommand DeleteCommand { get; }

    public RemindersViewModel(TaskService service)
    {
        this.service = service;
        NewCommand = new(() => Edit(null), () => !IsBusy);
        CancelCommand = new(() => { IsEditing = false; Message = ""; }, () => !IsBusy);
        RefreshCommand = new(LoadAsync, () => !IsBusy);
        SaveCommand = new(SaveAsync, () => !IsBusy && IsEditing);
        EditCommand = new(value => { if (value is SignalRow row) Edit(row.Item); return Task.CompletedTask; }, () => !IsBusy);
        DeleteCommand = new(value => { if (value is SignalRow row) DeleteRequested?.Invoke(row.Item); return Task.CompletedTask; }, () => !IsBusy);
        ToggleCommand = new(async value =>
        {
            if (value is not SignalRow row) return;
            var id = row.Item.Id;
            if (await WriteAsync(() => service.SetSignalEnabled(id, !row.Item.Enabled), "Состояние напоминания сохранено")) ToggleSaved?.Invoke(id);
        }, () => !IsBusy);
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
        foreach (var item in items) Signals.Add(new(item));
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
        RepeatIndex = item?.Schedule.Repeat == SignalRepeat.Hourly ? 1 : 0;
        foreach (var day in Days) day.Selected = item?.Schedule.Days.Contains(day.Day) ?? day.Day == DayOfWeek.Monday;
        Enabled = item?.Enabled ?? true;
        Message = ""; HasError = false; IsEditing = true;
        FocusEditor?.Invoke();
    }

    private async Task SaveAsync()
    {
        SignalSchedule schedule;
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
            else
            {
                InvalidField = "DaysInput";
                if (!Days.Any(x => x.Selected)) throw new ArgumentException("Выберите хотя бы один день недели.");
                InvalidField = "WeeklyTimeInput";
                if (!TimeOnly.TryParseExact(WeeklyTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out at)) throw new ArgumentException("Время по дням недели: ЧЧ:ММ, от 00:00 до 23:59.");
            }
            schedule = new(date, start, Hourly ? SignalRepeat.Hourly : SignalRepeat.Weekly, Days.Where(x => x.Selected).Select(x => x.Day).ToArray(), at, atMinute);
        }
        catch (ArgumentException ex) { HasError = true; Message = ex.Message; ValidationFailed?.Invoke(); return; }
        var savedTitle = Title; var savedDescription = Description; var savedEnabled = Enabled;
        if (await WriteAsync(() => service.SaveSignal(editingId, savedTitle, savedDescription, schedule, savedEnabled), "Напоминание сохранено")) IsEditing = false;
    }

    private async Task<bool> WriteAsync(Func<SignalReminder> write, string success)
    {
        IsBusy = true;
        try
        {
            var item = await Task.Run(write);
            var index = Signals.ToList().FindIndex(row => row.Item.Id == item.Id);
            if (index < 0) Signals.Add(new(item)); else Signals[index].Update(item);
            Changed(nameof(IsEmpty)); Changed(nameof(CountLabel));
            HasError = false; Message = success; ChangedStorage?.Invoke(); return true;
        }
        catch (ArgumentException ex) { HasError = true; Message = ex.Message; return false; }
        catch (Exception ex) when (MainViewModel.IsStorageError(ex)) { HasError = true; Message = "Не удалось сохранить напоминание. Ввод остаётся в форме; попробуйте ещё раз."; return false; }
        finally { IsBusy = false; }
    }
}
