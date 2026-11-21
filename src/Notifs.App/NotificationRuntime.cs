using System.Windows.Threading;
using Notifs.Core;
using Microsoft.Win32;

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
    private bool sleeping, locked, started, recoverOnNextPoll;
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
        service.CheckInChanged += InvalidateCheckIn;
        service.PersonalScheduleChanged += InvalidatePersonal;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.SessionSwitch += SessionChanged;
        timer.Tick += async (_, _) => await RunOnceAsync();
        main.Closed += MainClosed;
    }
    public void Start() { if (!started) recoverOnNextPoll = true; started = true; stopping = false; timer.Start(); }
    public async Task SuspendAsync()
    {
        stopping = true; timer.Stop();
        while (IsBusy) await Task.Delay(20);
    }

    public async Task RunOnceAsync()
    {
        if (disposed || stopping || sleeping || polling || model.IsBusy) return;
        polling = true;
        try
        {
            var reconcile = recoverOnNextPoll;
            var settings = await Task.Run(() => (Notifications: service.ReadNotificationSettings(), Monitors: service.ReadMonitorSettings()));
            var monitor = locked ? null : NotificationRouting.Select(settings.Monitors, WindowsNotificationMonitors.List(), WindowsNotificationMonitors.ForegroundFullscreenOnPrimary());
            var events = await Task.Run(() => service.PollNotifications(reconcile, suppress: monitor == null));
            recoverOnNextPoll = false;
            if (disposed) return;
            model.ClearNotificationError();
            output.SetMonitor(monitor);
            foreach (var notification in events)
            {
                var current = service.RefreshNotification(notification);
                if (monitor != null) delivery.Publish(current, settings.Notifications.Sound && current.Kind != NotificationKind.Missed);
                else delivery.ReceiveSuppressed(current, settings.Notifications.Sound);
            }
            main.RefreshReminderRows();
            if (events.Count > 0 && (model.IsCalendar || model.IsInProgress)) await model.RefreshSourcesAsync(); else model.RefreshTime();
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
        if (notification.Kind is NotificationKind.Summary or NotificationKind.Missed && action == "open-tasks")
        { await main.OpenTasksAsync(); delivery.Hide(notification.Id); return; }
        if (subject.Key.Type == "recurring")
        {
            if (action == "open") await main.OpenSignalAsync();
            else if (action == "complete") await Task.Run(() => service.CompleteRecurring(subject.Key.Id));
            else if (action == "snooze") await Task.Run(() => service.SnoozeRecurring(subject.Key.Id));
            else throw new ArgumentException("Действие для повторяющегося дела недоступно.");
            main.RefreshReminderRows();
            if (action != "open") await model.RefreshSourcesAsync();
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
            case "extend-check-in" when notification.Kind == NotificationKind.CheckIn:
                await Task.Run(() => service.ExtendCheckIn(subject.Key.Id, notification.SourceId)); model.RefreshTime(); break;
            case "snooze" when notification.Kind == NotificationKind.Personal:
                await Task.Run(() => service.SnoozePersonal(subject.Key.Id)); break;
            default: throw new ArgumentException("Источник действия пока не подключён.");
        }
        if (action != "open") await model.RefreshSourcesAsync();
    }

    private void Invalidate(EntityKey key, bool personalOnly)
    {
        main.Dispatcher.BeginInvoke(() =>
        {
            if (disposed) return;
            delivery.RemoveSubject(key, personalOnly);
        });
    }
    private void InvalidateCheckIn(Guid id) => main.Dispatcher.BeginInvoke(() => { if (!disposed) delivery.RemoveSubject(new("task", id), false, NotificationKind.CheckIn); });
    private void InvalidatePersonal(Guid id) => main.Dispatcher.BeginInvoke(() => { if (!disposed) delivery.RemoveSubject(new("task", id), false, NotificationKind.Personal); });
    private void PowerChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (disposed) return;
        main.Dispatcher.Invoke(() =>
        {
            if (disposed) return;
            if (args.Mode == PowerModes.Suspend)
            {
                sleeping = true; timer.Stop();
                try { service.CheckpointSessions(pause: true); model.RefreshTime(); }
                catch (Exception ex) when (MainViewModel.IsStorageError(ex)) { model.ReportNotificationError(); }
            }
            else if (args.Mode == PowerModes.Resume) { sleeping = false; recoverOnNextPoll = true; if (!stopping) timer.Start(); }
        });
    }
    private void SessionChanged(object sender, SessionSwitchEventArgs args)
    {
        if (disposed) return;
        main.Dispatcher.BeginInvoke(() =>
        {
            if (disposed) return;
            if (args.Reason == SessionSwitchReason.SessionLock) { locked = true; output.SetMonitor(null); }
            else if (args.Reason == SessionSwitchReason.SessionUnlock) locked = false;
        });
    }
    private void MainClosed(object? sender, EventArgs args) => Dispose();
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; timer.Stop();
        main.Closed -= MainClosed;
        service.TaskNotificationsChanged -= Invalidate;
        service.CheckInChanged -= InvalidateCheckIn;
        service.PersonalScheduleChanged -= InvalidatePersonal;
        SystemEvents.PowerModeChanged -= PowerChanged;
        SystemEvents.SessionSwitch -= SessionChanged;
        delivery.HideAll(); output.Dispose();
    }
}
