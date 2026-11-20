using System.Windows.Threading;
using Notifs.Core;

namespace Notifs.App;

/// <summary>Process-lifetime scheduling, independent of the current page and user input.</summary>
public sealed class NotificationRuntime : IDisposable
{
    private readonly TaskService service;
    private readonly MainViewModel model;
    private readonly MainWindow main;
    private readonly NativeNotificationOutput output;
    private readonly NotificationDelivery delivery;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool polling, disposed, stopping;
    private int activeActions;
    public bool IsBusy => polling || activeActions != 0;

    public NotificationRuntime(TaskService service, MainViewModel model, MainWindow main, NativeNotificationOutput? output = null)
    {
        this.service = service; this.model = model; this.main = main;
        this.output = output ?? new NativeNotificationOutput();
        delivery = new(this.output);
        this.output.HoverChanged = delivery.SetHovered;
        this.output.HideRequested = delivery.Hide;
        this.output.HideAllRequested = delivery.HideAll;
        this.output.ActionRequested = ActAsync;
        this.output.SnoozeMinutes = () => service.ReadNotificationSettings().SnoozeMinutes;
        service.TaskNotificationsChanged += Invalidate;
        timer.Tick += async (_, _) => await RunOnceAsync();
        main.Closed += MainClosed;
    }
    public void Start() { stopping = false; timer.Start(); }
    public async Task SuspendAsync()
    {
        stopping = true; timer.Stop();
        while (IsBusy) await Task.Delay(20);
    }

    public async Task RunOnceAsync()
    {
        if (disposed || stopping || polling || model.IsBusy) return;
        polling = true;
        try
        {
            var result = await Task.Run(() => (Events: service.PollPersonal().Concat(service.PollSignals()).Concat(service.PollSummary()).ToArray(), Settings: service.ReadNotificationSettings()));
            if (disposed) return;
            model.ClearNotificationError();
            foreach (var notification in result.Events)
            {
                if (notification.Kind == NotificationKind.Summary)
                {
                    delivery.Publish(service.RefreshSummary(notification), result.Settings.Sound);
                    continue;
                }
                var id = notification.Subjects[0].Key.Id;
                // A completion/deletion/snooze may have committed while the worker returned its batch.
                if (notification.Kind is NotificationKind.Signal or NotificationKind.Recurring)
                {
                    if (!service.IsSignalCurrent(notification)) continue;
                }
                else if (!service.List().Any(x => x.Id == id) || service.ReadPersonal(id) is null || service.IsSnoozed(id)) continue;
                delivery.Publish(notification, result.Settings.Sound);
            }
            main.RefreshReminderRows();
        }
        catch (Exception ex) when (MainViewModel.IsStorageError(ex)) { model.ReportNotificationError(); }
        finally { polling = false; }
    }

    private async Task ActAsync(NotificationEvent notification, NotificationSubject subject, string action)
    {
        if (disposed || stopping) throw new System.IO.IOException("Приложение завершает работу. Повторите действие после открытия.");
        activeActions++;
        try { await ApplyActionAsync(notification, subject, action); }
        finally { activeActions--; }
    }

    private async Task ApplyActionAsync(NotificationEvent notification, NotificationSubject subject, string action)
    {
        if (notification.Kind == NotificationKind.Summary && action == "open-tasks")
        { await main.OpenTasksAsync(); delivery.Hide(notification.Id); return; }
        if (subject.Key.Type == "recurring")
        {
            if (action == "open") await main.OpenSignalAsync();
            else if (action == "complete") await Task.Run(() => service.CompleteRecurring(subject.Key.Id));
            else if (action == "snooze") await Task.Run(() => service.SnoozeRecurring(subject.Key.Id));
            else throw new ArgumentException("Действие для повторяющегося дела недоступно.");
            main.RefreshReminderRows();
            return;
        }
        if (subject.Key.Type == "signal")
        {
            if (action == "open") await main.OpenSignalAsync();
            else if (action == "snooze") await Task.Run(() => service.SnoozeSignal(subject.Key.Id));
            else throw new ArgumentException("Простой сигнал не требует выполнения.");
            return;
        }
        if (subject.Key.Type != "task") throw new ArgumentException("Источник действия пока не подключён.");
        switch (action)
        {
            case "open": await main.OpenTaskAsync(subject.Key.Id); break;
            case "complete": await model.CompleteFromNotificationAsync(subject.Key.Id); break;
            case "snooze" when notification.Kind == NotificationKind.Personal:
                await Task.Run(() => service.SnoozePersonal(subject.Key.Id)); break;
            default: throw new ArgumentException("Источник действия пока не подключён.");
        }
    }

    private void Invalidate(EntityKey key, bool personalOnly)
    {
        main.Dispatcher.BeginInvoke(() => { if (!disposed) delivery.RemoveSubject(key, personalOnly); });
    }
    private void MainClosed(object? sender, EventArgs args) => Dispose();
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; timer.Stop();
        main.Closed -= MainClosed;
        service.TaskNotificationsChanged -= Invalidate;
        delivery.HideAll(); output.Dispose();
    }
}
