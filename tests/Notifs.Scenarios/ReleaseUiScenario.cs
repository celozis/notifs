using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Notifs.App;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static partial class UiScenario
{
    private static async Task RunReleaseAsync(string path)
    {
        var clock = new FieldClock(new(2026, 11, 18, 7, 0, 0, TimeSpan.Zero));
        void At(int day, int hour = 12, int minute = 0) => clock.Set(new DateTimeOffset(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(5)).ToUniversalTime());
        var service = new TaskService(path, clock); service.Initialize();
        At(16, 15); var practice = service.Create("Закончить практическую работу", "### Остался последний шаг\n\n- Проверить расчёты\n- Оформить выводы\n- Отправить преподавателю", new(new(2026, 11, 18), new(20, 0)), 420, ImportanceRank.S);
        At(17, 10); var homework = service.Create("Отправить домашнюю работу", "Проверить вложения, прикрепить файл и отправить работу в личном кабинете.", new(new(2026, 11, 18), new(14, 0)), 40, ImportanceRank.A);
        At(18, 9); service.Create("Сдать отчёт по проекту", "", new(new(2026, 11, 24), new(16, 0)), 240, ImportanceRank.A);
        At(10, 15); service.Create("Пройти курс по C#", "", new(new(2026, 12, 18), new(18, 0)), 1200);
        At(14); service.Create("Навести порядок на рабочем столе", "", null, 4320, ImportanceRank.C);
        At(18, 9); var floor = service.Create("Помыть пол", "Сначала убрать вещи в комнате, затем помыть пол на кухне.", null, 45, ImportanceRank.D);
        At(16, 10); var done = service.Create("Записаться на следующую неделю", "", new(new(2026, 11, 17), new(18, 0)), 15); At(17, 18); service.Complete(done.Id);
        At(4, 15);
        var linen = service.SaveSignal(null, "Сменить постельное бельё", "", new(new(2026, 11, 4), new(15, 0), SignalRepeat.AfterCompletion, [], new(15, 0)), true, true);
        service.CompleteRecurring(linen.Current!.Id);
        linen = service.SaveSignal(linen.Id, linen.Title, "", linen.Schedule with { FirstDate = new(2026, 11, 18) }, true, true);
        At(18);
        service.SaveSignal(null, "Размяться и отдохнуть от экрана", "Небольшой перерыв пойдёт на пользу.", new(new(2026, 11, 18), new(12, 0), SignalRepeat.Hourly, [], new(12, 0)), true);
        var model = new MainViewModel(service); var main = new MainWindow(model);
        main.Show(); await UntilAsync(() => !model.IsBusy); await model.LoadAsync(practice.Id, true); await SizeAsync(main, 1010, 1060);
        async Task Capture(string name) { await IdleAsync(); SaveImage(main, "release-" + name + ".png"); }
        void Escape() => main.InputBindings.OfType<KeyBinding>().Single(x => x.Key == Key.Escape).Command.Execute(null);
        await Capture("tasks"); main.FiltersCommand.Execute(null); await Capture("filters"); Escape(); await IdleAsync();
        await ExecuteAsync(model.StartSessionCommand, model); await model.OpenInProgressAsync(); await Capture("in-progress");
        await ExecuteAsync(model.PauseSessionCommand, model); await Capture("paused");
        await model.SelectTaskAsync(homework.Id); await ExecuteAsync(model.StartSessionCommand, model);
        main.NewCommand.Execute(null); await Capture("editor");
        var view = (EditorView)((ContentControl)main.FindName("EditorHost")).Content;
        ((EditorViewModel)view.DataContext).Title = "Сохранённый черновик";
        main.CancelEditorCommand.Execute(null);
        await main.OpenRemindersAsync(); await Capture("reminders");
        var reminders = (RemindersViewModel)((RemindersView)((ContentControl)main.FindName("RemindersHost")).Content).DataContext;
        reminders.EditCommand.Execute(reminders.Signals.Single(x => x.Item.Id == linen.Id)); await Capture("reminder-editor");
        reminders.CancelCommand.Execute(null); main.SettingsCommand.Execute(null); await Capture("settings");
        main.CalendarCommand.Execute(null); await UntilAsync(() => !model.IsBusy); await SizeAsync(main, 1010, 1270); await Capture("week");
        await model.ChangeCalendarAsync(model.Calendar with { IsMonth = true }); await Capture("month");
        await main.ShowCalendarPeriodAsync(new(new(2026, 11, 15), new(2026, 11, 16))); await Capture("range"); Escape(); await IdleAsync();
        await main.OpenTaskAsync(homework.Id); await SizeAsync(main, 1010, 1060);
        using var output = new NativeNotificationOutput(); var delivery = new NotificationDelivery(output);
        output.HideRequested = delivery.Hide; output.HideAllRequested = delivery.HideAll; output.HoverChanged = delivery.SetHovered;
        delivery.Publish(new(Guid.NewGuid(), NotificationKind.Personal, practice.Id.ToString(), [new(new("task", practice.Id), practice.Title, "Создана 2 дн. назад")], [new("complete", "Готово"), new("snooze", "Отложить")]), false);
        delivery.Publish(new(Guid.NewGuid(), NotificationKind.Signal, "signal", [new(new("signal", Guid.NewGuid()), "Размяться и отдохнуть от экрана", "Небольшой перерыв пойдёт на пользу.")]), false);
        await Task.Delay(220); SaveImage(output.Window, "release-notifications.png"); delivery.HideAll();
        At(18, 12, 40); var question = service.PollCheckIns().Single(); delivery.Publish(question, false); model.RefreshTime();
        await Task.Delay(220); SaveImage(output.Window, "release-check-in.png"); delivery.HideAll();
        service.PauseSession(homework.Id); service.ExtendCheckIn(homework.Id, question.SourceId); At(18, 13, 10); service.ResumeSession(homework.Id); At(18, 13, 40);
        service.PollCheckIns(); service.PauseSession(homework.Id); service.Complete(practice.Id);
        await model.SelectTaskAsync(floor.Id); main.DeleteCommand.Execute(null); await Capture("delete-confirmation"); main.CancelDeleteCommand.Execute(null);
        main.NewCommand.Execute(null); await IdleAsync(); view = (EditorView)((ContentControl)main.FindName("EditorHost")).Content;
        var input = (TextBox)view.FindName("DescriptionInput"); input.Text = "Проверка выделения"; input.Focus(); input.Select(0, 8); await Capture("markdown-editor"); main.CancelEditorCommand.Execute(null);
        foreach (var (name, width) in new[] { (768, 718), (375, 357), (320, 304) })
        {
            await model.SelectTaskAsync(floor.Id); await SizeAsync(main, width, 1060); main.BackToListCommand.Execute(null); await Capture("width-" + name);
        }
        if (Environment.GetEnvironmentVariable("NOTIFS_QA_INTERACTIVE") == "1")
        {
            await SizeAsync(main, 1010, 920); await model.OpenCalendarAsync(); await model.ChangeCalendarAsync(model.Calendar with { IsMonth = true });
            main.Title = "Notifs QA — ручная проверка диапазона";
            var arrivals = 0;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) =>
            {
                var sequence = ++arrivals;
                delivery.Publish(new(Guid.NewGuid(), NotificationKind.Signal, sequence.ToString(), [new(new("signal", Guid.NewGuid()), "Проверка наведения · " + sequence, "Новые события ждут ухода указателя.")]), false);
                Console.WriteLine("HOVER arrival=" + sequence + " visible=" + string.Join(",", output.VisibleEvents.Select(x => x.SourceId)));
            };
            timer.Start();
            var closed = new TaskCompletionSource(); main.Closed += (_, _) => closed.TrySetResult();
            try { await closed.Task; } finally { timer.Stop(); delivery.HideAll(); }
        }
        else main.Close();
        Console.WriteLine("CAPTURED eighteen requested native reference states; inspect release-*.png");
    }
}
