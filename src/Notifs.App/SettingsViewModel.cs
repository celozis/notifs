using System.Globalization;
using Notifs.Core;

namespace Notifs.App;

public sealed class SettingsViewModel : Observable
{
    private readonly TaskService service;
    private readonly WindowsAutoStart autoStart;
    private bool startWithWindows;
    public bool StartWithWindows { get => startWithWindows; set { startWithWindows = value; Changed(); } }
    private string minutes = "30", message = "";
    private bool sound = true, busy;
    private bool summaryEnabled = true;
    private string summaryMinutes = "180", summaryTaskCount = "3", firstDate = "", firstTime = "";
    public bool SummaryEnabled { get => summaryEnabled; set { summaryEnabled = value; Changed(); } }
    public string SummaryMinutes { get => summaryMinutes; set { summaryMinutes = value; Changed(); } }
    public string SummaryTaskCount { get => summaryTaskCount; set { summaryTaskCount = value; Changed(); } }
    public string SummaryFirstDate { get => firstDate; set { firstDate = value; Changed(); } }
    public string SummaryFirstTime { get => firstTime; set { firstTime = value; Changed(); } }
    public event Action<string>? InvalidField;
    public string SnoozeMinutes { get => minutes; set { minutes = value; Changed(); } }
    public bool Sound { get => sound; set { sound = value; Changed(); } }
    public string Message { get => message; private set { message = value; Changed(); Changed(nameof(HasError)); } }
    public bool HasError { get; private set; }
    public bool IsBusy { get => busy; private set { busy = value; Changed(); Changed(nameof(CanEdit)); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public bool CanEdit => !IsBusy;
    public AsyncCommand SaveCommand { get; }
    public SettingsViewModel(TaskService service, WindowsAutoStart? autoStart = null)
    {
        this.service = service;
        this.autoStart = autoStart ?? new WindowsAutoStart();
        try
        {
            startWithWindows = this.autoStart.IsEnabled;
            var saved = service.ReadNotificationSettings(); sound = saved.Sound; minutes = saved.SnoozeMinutes.ToString();
            var summary = service.ReadSummarySettings();
            summaryEnabled = summary.Enabled; summaryMinutes = summary.IntervalMinutes.ToString(); summaryTaskCount = summary.TaskCount.ToString();
            firstDate = summary.FirstDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture); firstTime = summary.FirstTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (IsSettingsError(ex)) { HasError = true; Message = "Не удалось прочитать настройки. Повторите открытие раздела."; }
        SaveCommand = new(SaveAsync, () => !IsBusy);
    }
    private async Task SaveAsync()
    {
        HasError = false; Message = "";
        if (!int.TryParse(SnoozeMinutes, out var duration) || duration is <= 0 or > 525600)
        { Invalid("SnoozeInput", "Откладывание: целое число от 1 до 525600 минут."); return; }
        if (!int.TryParse(SummaryMinutes, out var interval) || interval < 1)
        { Invalid("SummaryMinutesInput", "Интервал сводки: положительное целое число минут."); return; }
        if (!int.TryParse(SummaryTaskCount, out var count) || count is < 1 or > 10)
        { Invalid("SummaryCountInput", "Задач в карточке: целое число от 1 до 10."); return; }
        if (!DateOnly.TryParseExact(SummaryFirstDate.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        { Invalid("SummaryDateInput", "Первая сводка: введите дату в формате ДД.ММ.ГГГГ."); return; }
        if (!TimeOnly.TryParseExact(SummaryFirstTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var wallTime))
        { Invalid("SummaryTimeInput", "Первая сводка: введите время в формате ЧЧ:ММ."); return; }
        var settings = new NotificationSettings(Sound, duration);
        var summary = new SummarySettings(SummaryEnabled, interval, count, date, wallTime);
        IsBusy = true;
        try
        {
            await Task.Run(() =>
            {
                var previous = autoStart.ReadCommand();
                var desired = StartWithWindows ? autoStart.LaunchCommand : null;
                var changed = !string.Equals(previous, desired, StringComparison.Ordinal);
                if (changed) autoStart.WriteCommand(desired);
                try { service.SaveSettings(settings, summary); }
                catch
                {
                    if (changed) autoStart.WriteCommand(previous);
                    throw;
                }
            });
            Message = "Настройки сохранены";
        }
        catch (ArgumentException ex) { Invalid("SummaryTimeInput", ex.Message); }
        catch (Exception ex) when (IsSettingsError(ex)) { HasError = true; Message = "Не удалось сохранить настройки или автозапуск Windows. Ввод остаётся в форме; попробуйте ещё раз."; }
        finally { IsBusy = false; }
    }
    private void Invalid(string field, string message) { HasError = true; Message = message; InvalidField?.Invoke(field); }
    private static bool IsSettingsError(Exception error) => MainViewModel.IsStorageError(error) || error is System.Security.SecurityException;
}
