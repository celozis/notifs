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
    private bool polling, disposed;

    public NotificationRuntime(TaskService service, MainViewModel model, MainWindow main, NativeNotificationOutput? output = null)
    {
        this.service = service; this.model = model; this.main = main;
        this.output = output ?? new NativeNotificationOutput();
        delivery = new(this.output);
        this.output.HideRequested = delivery.Hide;
        this.output.HideAllRequested = delivery.HideAll;
        this.output.ActionRequested = ActAsync;
        this.output.SnoozeMinutes = () => service.ReadNotificationSettings().SnoozeMinutes;
        service.TaskNotificationsChanged += Invalidate;
        timer.Tick += async (_, _) => await RunOnceAsync();
        main.Closed += MainClosed;
    }
    public void Start() => timer.Start();

    public async Task RunOnceAsync()
    {
        if (disposed || polling || model.IsBusy) return;
        polling = true;
        try
        {
            var result = await Task.Run(() => (Events: service.PollPersonal().Concat(service.PollSignals()).ToArray(), Settings: service.ReadNotificationSettings()));
            if (disposed) return;
            model.ClearNotificationError();
            foreach (var notification in result.Events)
            {
                var id = notification.Subjects[0].Key.Id;
                // A completion/deletion/snooze may have committed while the worker returned its batch.
                if (notification.Kind == NotificationKind.Signal)
                {
                    if (!service.IsSignalCurrent(notification)) continue;
                }
                else if (!service.List().Any(x => x.Id == id) || service.ReadPersonal(id) is null || service.IsSnoozed(id)) continue;
                delivery.Publish(notification, result.Settings.Sound);
            }
        }
        catch (Exception ex) when (MainViewModel.IsStorageError(ex)) { model.ReportNotificationError(); }
        finally { polling = false; }
    }

    private async Task ActAsync(NotificationEvent notification, NotificationSubject subject, string action)
    {
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
