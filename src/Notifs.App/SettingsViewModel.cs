using Notifs.Core;

namespace Notifs.App;

public sealed class SettingsViewModel : Observable
{
    private readonly TaskService service;
    private string minutes = "30", message = "";
    private bool sound = true, busy;
    public string SnoozeMinutes { get => minutes; set { minutes = value; Changed(); } }
    public bool Sound { get => sound; set { sound = value; Changed(); } }
    public string Message { get => message; private set { message = value; Changed(); Changed(nameof(HasError)); } }
    public bool HasError { get; private set; }
    public bool IsBusy { get => busy; private set { busy = value; Changed(); Changed(nameof(CanEdit)); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public bool CanEdit => !IsBusy;
    public AsyncCommand SaveCommand { get; }
    public SettingsViewModel(TaskService service)
    {
        this.service = service;
        try { var saved = service.ReadNotificationSettings(); sound = saved.Sound; minutes = saved.SnoozeMinutes.ToString(); }
        catch (Exception ex) when (MainViewModel.IsStorageError(ex)) { HasError = true; Message = "Не удалось прочитать настройки. Повторите открытие раздела."; }
        SaveCommand = new(SaveAsync, () => !IsBusy);
    }
    private async Task SaveAsync()
    {
        HasError = false; Message = "";
        if (!int.TryParse(SnoozeMinutes, out var duration) || duration is <= 0 or > 525600)
        { HasError = true; Message = "Введите целое число от 1 до 525600 минут."; return; }
        var settings = new NotificationSettings(Sound, duration);
        IsBusy = true;
        try { await Task.Run(() => service.SaveNotificationSettings(settings)); Message = "Настройки сохранены"; }
        catch (Exception ex) when (MainViewModel.IsStorageError(ex)) { HasError = true; Message = "Не удалось сохранить настройки. Ввод остаётся в форме; попробуйте ещё раз."; }
        finally { IsBusy = false; }
    }
}
