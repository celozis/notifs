using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Notifs.App;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static partial class UiScenario
{
    private static async Task RunFeaturesAsync(string path)
    {
        await CheckCalendarMembershipRefreshAsync(path + ".membership");
        var clock = new FieldClock(new(2026, 11, 17, 5, 0, 0, TimeSpan.Zero));
        var service = new TaskService(path, clock); service.Initialize();
        var a = service.Create("Закончить практическую работу", "### Остался последний шаг\n\n- Проверить расчёты\n- Оформить выводы\n- Отправить преподавателю", new(new(2026, 11, 18), new(20, 0)), 420, ImportanceRank.S);
        var b = service.Create("Отправить домашнюю работу", "Проверить вложения, прикрепить файл и отправить работу в личном кабинете.", new(new(2026, 11, 18), new(14, 0)), 40, ImportanceRank.A);
        service.Create("Сдать отчёт по проекту", "", new(new(2026, 11, 24), new(16, 0)), 240, ImportanceRank.A);
        service.Create("Пройти курс по C#", "", new(new(2026, 12, 18), new(18, 0)), 1200);
        service.Create("Навести порядок на рабочем столе", "", null, 4320, ImportanceRank.C);
        service.Create("Помыть пол", "", null, 45, ImportanceRank.D);
        var done = service.Create("Записаться на следующую неделю", ""); service.Complete(done.Id);
        clock.Set(new(2026, 11, 18, 7, 0, 0, TimeSpan.Zero));
        var model = new MainViewModel(service); var main = new MainWindow(model); main.Show(); await UntilAsync(() => !model.IsBusy);
        await SizeAsync(main, 1010, 1060); await model.LoadAsync(a.Id, true);
        await ExecuteAsync(model.StartSessionCommand, model);
        await model.OpenInProgressAsync(); await IdleAsync();
        Assert(model.IsInProgress && model.Tasks.Single().SessionRunning, "real WPF In Progress shows the started ordinary task without copying it");
        SaveImage(main, "issue-16-in-progress.png");
        var pause = Descendants<Button>(main).Single(x => Equals(x.Content, "Сделать паузу")); pause.Focus();
        clock.Set(new(2026, 11, 18, 7, 10, 0, TimeSpan.Zero)); model.RefreshTime();
        Assert(ReferenceEquals(Keyboard.FocusedElement, pause), "timer updates preserve keyboard focus on the existing session action");
        await ExecuteAsync(model.PauseSessionCommand, model); await IdleAsync(); SaveImage(main, "issue-16-paused.png");
        await model.SelectTaskAsync(b.Id); await ExecuteAsync(model.StartSessionCommand, model);
        Assert(service.ReadSession(a.Id)!.RunningSince == null && model.InProgressCount == 2, "second WPF start atomically pauses first and updates the navigation count");
        SaveImage(main, "issue-16-tasks.png");
        using var output = new NativeNotificationOutput(); using var runtime = new NotificationRuntime(service, model, main, output);
        clock.Set(new(2026, 11, 18, 7, 50, 0, TimeSpan.Zero));
        await runtime.RunOnceAsync(); await IdleAsync();
        Assert(output.VisibleEvents.Single().Kind == NotificationKind.CheckIn, "runtime delivers the real native result question after forty active minutes");
        await Task.Delay(220); SaveImage(output.Window, "issue-17-check-in.png");
        var extend = Descendants<Button>(output.Window).Single(x => Equals(x.Content, "Ещё 30 минут"));
        extend.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await UntilAsync(() => output.VisibleEvents.Count == 0);
        Assert(service.ReadSession(b.Id) is { QuestionAsked: false } session && session.TargetActiveTicks == TimeSpan.FromMinutes(70).Ticks, "native extension uses thirty active minutes without changing estimate");
        await model.OpenInProgressAsync(); model.Selected = model.Tasks.Single(x => x.Item.Id == a.Id);
        await ExecuteAsync(model.CancelSessionCommand, model);
        Assert(model.Tasks.All(x => x.Item.Id != a.Id) && model.Tasks.Single().Item.Id == b.Id, "cancel immediately removes an unstarted task from In Progress");
        var calendarOnly = service.Create("Только будущий вопрос", "", new(new(2026, 11, 18)), 7200);
        await model.SelectTaskAsync(calendarOnly.Id); await ExecuteAsync(model.StartSessionCommand, model);
        await model.OpenCalendarAsync(); await model.ChangeCalendarAsync(model.Calendar with { SelectedDay = new(2026, 11, 23) });
        Assert(model.Tasks.Single().Item.Id == calendarOnly.Id, "calendar can contain a task solely by its future question");
        await ExecuteAsync(model.PauseSessionCommand, model);
        Assert(model.Tasks.Count == 0 && model.Calendar.SelectedDay == new DateOnly(2026, 11, 23), "pausing removes obsolete calendar membership while preserving selected period");
        await model.SelectTaskAsync(b.Id); await ExecuteAsync(model.ResumeSessionCommand, model);
        await model.OpenCalendarAsync(); await model.ChangeCalendarAsync(model.Calendar with { IsMonth = true });
        await main.ShowCalendarPeriodAsync(new(new(2026, 11, 15), new(2026, 12, 6))); await IdleAsync(); SaveImage(main, "issue-18-range.png");
        var host = (ContentControl)main.FindName("TaskPanelHost");
        Descendants<Button>(host).Single(x => Equals(x.Content, "Перейти к этому периоду")).Command.Execute(null); await UntilAsync(() => !model.IsBusy); await IdleAsync();
        Assert(model.Calendar.Period.End == new DateOnly(2026, 12, 6) && !model.Calendar.IsMonth, "period transition opens first week without narrowing its long selection");
        var reminder = service.SaveSignal(null, "Сменить наволочку", "", new(new(2026, 11, 19), new(15, 0), SignalRepeat.AfterCompletion, [], new(15, 0)), true, true);
        await model.LoadAsync(reapplyFilter: true); await IdleAsync();
        Assert(model.VisibleRows.OfType<CalendarReminderRow>().Single().Entry.Reminder!.ScheduleId == reminder.Id, "WPF period list contains a marked recurring plan without an ordinary rank");
        await model.ChangeCalendarAsync(model.Calendar with { IsMonth = true }); SaveImage(main, "issue-19-month.png");
        await main.ShowCalendarPeriodAsync(model.Calendar.Period); await IdleAsync(); SaveImage(main, "issue-19-period.png");
        var escape = main.InputBindings.OfType<KeyBinding>().Single(x => x.Key == Key.Escape); escape.Command.Execute(null); await IdleAsync();
        main.SettingsCommand.Execute(null); await IdleAsync(); Assert(((SettingsViewModel)((SettingsView)((ContentControl)main.FindName("SettingsHost")).Content).DataContext).HasError == false, "native settings load monitor choices and startup without error"); SaveImage(main, "issue-15-settings.png");
        foreach (var width in new[] { 768, 375, 320 })
        {
            await SizeAsync(main, width, 1060); SaveImage(main, $"issue-15-settings-{width}.png");
            await model.SelectTaskAsync(b.Id); await main.OpenTaskAsync(b.Id); await IdleAsync();
            SaveImage(main, $"issue-16-active-{width}.png"); main.SettingsCommand.Execute(null); await IdleAsync();
        }
        main.Close();
        await RunMonitorNativeAsync();
    }

    private static async Task CheckCalendarMembershipRefreshAsync(string path)
    {
        var clock = new FieldClock(new(2026, 11, 18, 18, 39, 0, TimeSpan.Zero));
        var service = new TaskService(path, clock); service.Initialize();
        var questionTask = service.Create("Вопрос переходит на завтра", "", new(new(2026, 11, 18)), 1);
        var other = service.Create("Другая задача завтра", "", new(new(2026, 11, 19)));
        var model = new MainViewModel(service); await model.LoadAsync();
        service.StartSession(questionTask.Id); clock.Set(new(2026, 11, 18, 18, 40, 0, TimeSpan.Zero));
        var question = service.PollCheckIns().Single();
        await model.OpenCalendarAsync(); await model.ChangeCalendarAsync(model.Calendar with { SelectedDay = new(2026, 11, 19) });
        var existing = model.Tasks.Single();
        Assert(existing.Item.Id == other.Id, "tomorrow initially contains only its existing task");
        service.ExtendCheckIn(questionTask.Id, question.SourceId); await model.RefreshSourcesAsync();
        Assert(model.Tasks.Count == 2 && model.Tasks.Any(x => x.Item.Id == questionTask.Id) && ReferenceEquals(existing, model.Tasks.Single(x => x.Item.Id == other.Id)) && model.Calendar.SelectedDay == new DateOnly(2026, 11, 19),
            "extending across midnight adds new calendar membership without replacing existing rows or period");
    }

    private static async Task RunMonitorNativeAsync()
    {
        var monitors = WindowsNotificationMonitors.List();
        Console.WriteLine("NATIVE MONITORS " + string.Join(" | ", monitors.Select(x => x.Name + " " + x.Bounds + " work=" + x.WorkArea)));
        var input = new TextBox { Text = "Фокус остаётся здесь" }; var typing = new Window { Title = "Notifs QA — foreground", Width = 500, Height = 180, Content = input };
        typing.Show(); typing.Activate(); input.Focus(); await IdleAsync();
        using var output = new NativeNotificationOutput(); var delivery = new NotificationDelivery(output);
        output.HideAllRequested = delivery.HideAll; output.HideRequested = delivery.Hide; output.HoverChanged = delivery.SetHovered;
        var a = new NotificationEvent(Guid.NewGuid(), NotificationKind.Personal, "a", [new(new("task", Guid.NewGuid()), "Закончить практическую работу", "Создана вчера")], [new("complete", "Готово"), new("snooze", "Отложить")]);
        var b = new NotificationEvent(Guid.NewGuid(), NotificationKind.Signal, "b", [new(new("signal", Guid.NewGuid()), "Размяться и отдохнуть от экрана", "Небольшой перерыв пойдёт на пользу.")]);
        var foreground = NativeForeground(); delivery.Publish(a, false); delivery.Publish(b, false); await IdleAsync();
        foreach (var monitor in monitors)
        {
            output.SetMonitor(monitor); await IdleAsync(); await Task.Delay(220);
            Assert(NativeForeground() == foreground && ReferenceEquals(Keyboard.FocusedElement, input), "native placement on " + monitor.Name + " preserves system foreground and keyboard focus");
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(output.Window);
            var point = output.Window.PointToScreen(new Point());
            Assert(Math.Abs(point.X + output.Window.ActualWidth * dpi.DpiScaleX - monitor.WorkArea.Right + 22 * dpi.DpiScaleX) <= 2 && Math.Abs(point.Y + output.Window.ActualHeight * dpi.DpiScaleY - monitor.WorkArea.Bottom + 22 * dpi.DpiScaleY) <= 2, "native stack fits selected physical work area at DPI " + dpi.PixelsPerDip);
            SaveImage(output.Window, monitor.IsPrimary ? "issue-15-primary.png" : "issue-15-secondary.png");
        }
        var originalCards = ((StackPanel)output.Window.Content).Children.Cast<UIElement>().ToArray();
        output.Window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        foreach (var index in new[] { 1, 2, 3 }) delivery.Publish(b with { Id = Guid.NewGuid(), SourceId = index.ToString() }, false);
        Assert(((StackPanel)output.Window.Content).Children.Cast<UIElement>().SequenceEqual(originalCards), "native hover event wiring preserves the original card controls while three events wait");
        output.Window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent }); await IdleAsync();
        Assert(output.VisibleEvents.Select(x => x.SourceId).SequenceEqual(new[] { "2", "3" }), "native hover departure renders only the two newest events");
        var primary = monitors.First(x => x.IsPrimary);
        typing.WindowStyle = WindowStyle.SingleBorderWindow; typing.WindowState = WindowState.Maximized; typing.Activate(); await IdleAsync();
        Assert(!WindowsNotificationMonitors.ForegroundFullscreenOnPrimary(), "ordinary maximized foreground is not fullscreen");
        typing.WindowState = WindowState.Normal; typing.WindowStyle = WindowStyle.None; typing.ResizeMode = ResizeMode.NoResize;
        var handle = new WindowInteropHelper(typing).Handle;
        PlaceNative(handle, 0, primary.Bounds.Left, primary.Bounds.Top, primary.Bounds.Right - primary.Bounds.Left, primary.Bounds.Bottom - primary.Bounds.Top, 0x0040); typing.Activate(); await IdleAsync();
        Assert(WindowsNotificationMonitors.ForegroundFullscreenOnPrimary(), "real borderless foreground covering primary is fullscreen");
        typing.WindowState = WindowState.Minimized; await IdleAsync();
        Assert(!WindowsNotificationMonitors.ForegroundFullscreenOnPrimary(), "minimized borderless window cannot trigger fullscreen routing");
        typing.Close();
    }
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint NativeForeground();
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")] private static extern bool PlaceNative(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
