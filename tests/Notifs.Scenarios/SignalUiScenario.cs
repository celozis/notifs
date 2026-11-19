using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using Notifs.App;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static partial class UiScenario
{
    private static async Task RunSignalsAsync(string databasePath)
    {
        var clock = new SignalScenarios.Clock(new(2026, 11, 18, 7, 0, 0, TimeSpan.Zero));
        var service = new TaskService(databasePath, clock);
        service.Initialize();
        var task = service.Create("Обычная задача", "");
        var draft = new TaskDraft("Новый черновик", "Нельзя терять", "", "", "", "", ImportanceRank.B);
        service.SaveDraft(null, draft);
        var model = new MainViewModel(service);
        var main = new MainWindow(model);
        main.Show(); await UntilAsync(() => !model.IsBusy);
        await SizeAsync(main, 1010, 930);
        await main.OpenRemindersAsync(); await IdleAsync();
        var view = (RemindersView)((ContentControl)main.FindName("RemindersHost")).Content;
        var reminders = (RemindersViewModel)view.DataContext;
        Assert(reminders.IsEmpty && reminders.IsList, "reminders navigation opens the real empty schedule list");
        SaveImage(main, "issue-5-empty.png");
        reminders.NewCommand.Execute(null); await IdleAsync();
        Assert(ReferenceEquals(Keyboard.FocusedElement, view.FindName("TitleInput")), "new reminder focuses its title in the main window");
        reminders.Description = "Небольшой перерыв пойдёт на пользу.";
        reminders.SaveCommand.Execute(null);
        Assert(reminders.IsEditing && reminders.Message.Contains("название") && reminders.Description.Contains("перерыв"), "invalid reminder title retains all entered text");
        reminders.Title = "Размяться и отдохнуть от экрана";
        reminders.FirstDate = "18.11.2026"; reminders.FirstTime = "12:25";
        reminders.RepeatIndex = 1; reminders.Minute = "60";
        reminders.SaveCommand.Execute(null);
        Assert(reminders.Message.Contains("0 до 59") && service.ListSignals().Count == 0, "minute 60 is rejected without creating a reminder");
        reminders.Minute = "25";
        reminders.FirstTime = "25:00"; reminders.SaveCommand.Execute(null);
        Assert(reminders.Message.Contains("ЧЧ:ММ") && reminders.Minute == "25", "invalid wall-clock time preserves hourly minute");
        reminders.FirstTime = "12:25";
        reminders.RepeatIndex = 0;
        foreach (var day in reminders.Days) day.Selected = false;
        reminders.SaveCommand.Execute(null);
        Assert(reminders.Message.Contains("день недели"), "weekly form requires at least one day");
        reminders.Days[0].Selected = true;
        reminders.RepeatIndex = 1;
        var afterCompletion = ((ComboBox)view.FindName("RepeatInput")).Items.Cast<ComboBoxItem>().Single(x => Equals(x.Content, "После выполнения"));
        Assert(!afterCompletion.IsEnabled, "simple signal cannot select interval after completion");
        reminders.SaveCommand.Execute(null);
        await UntilAsync(() => !reminders.IsBusy && !model.IsBusy && reminders.IsList);
        reminders.EditCommand.Execute(reminders.Signals.Single()); await IdleAsync();
        SaveImage(main, "issue-5-editor.png");
        foreach (var width in new[] { 768, 375, 320 })
        {
            await SizeAsync(main, width, 930);
            foreach (var scroll in Descendants<ScrollViewer>(view).Where(x => x.IsVisible))
                Assert(scroll.ExtentWidth <= scroll.ViewportWidth + 1, "reminder form has no horizontal overflow at " + width);
            SaveImage(main, $"issue-5-editor-{width}.png");
        }
        await SizeAsync(main, 1010, 930);
        reminders.SaveCommand.Execute(null);
        await UntilAsync(() => !reminders.IsBusy && !model.IsBusy && reminders.IsList);
        var id = service.ListSignals().Single().Id;
        Assert(service.ListAll().Single() == task && service.ReadDraft(null) == draft, "saving a reminder preserves ordinary task and independent task draft");
        SaveImage(main, "issue-5-reminders.png");
        foreach (var width in new[] { 375, 320 })
        {
            await SizeAsync(main, width, 930);
            foreach (var scroll in Descendants<ScrollViewer>(view).Where(x => x.IsVisible))
                Assert(scroll.ExtentWidth <= scroll.ViewportWidth + 1, "reminder list has no horizontal overflow at " + width);
            SaveImage(main, $"issue-5-reminders-{width}.png");
        }
        await SizeAsync(main, 1010, 930);
        reminders.EditCommand.Execute(reminders.Signals.Single()); await IdleAsync();
        reminders.Title = "Не сохранять";
        reminders.CancelCommand.Execute(null);
        Assert(service.ListSignals().Single().Title.Contains("Размяться"), "cancel leaves the saved rule unchanged");
        reminders.EditCommand.Execute(reminders.Signals.Single()); await IdleAsync();
        reminders.Description = "Ввод переживает ошибку записи";
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_signal_save BEFORE UPDATE ON signal_reminders BEGIN SELECT RAISE(ABORT,'blocked'); END";
            command.ExecuteNonQuery();
        }
        reminders.SaveCommand.Execute(null); await UntilAsync(() => !reminders.IsBusy);
        Assert(reminders.IsEditing && reminders.Description.Contains("ошибку") && reminders.Message.Contains("Не удалось сохранить") && service.ListSignals().Single().Description.Contains("перерыв"), "SQLite write failure retains signal form and previous committed description");
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TRIGGER fail_signal_save"; command.ExecuteNonQuery();
        }
        reminders.CancelCommand.Execute(null);
        using var output = new NativeNotificationOutput();
        using var runtime = new NotificationRuntime(service, model, main, output);
        clock.Advance(TimeSpan.FromMinutes(25));
        await runtime.RunOnceAsync(); await Task.Delay(250);
        Assert(output.VisibleEvents.Single().Kind == NotificationKind.Signal && !Descendants<Button>(output.Window).Any(x => Equals(x.Content, "Готово")), "real native signal card is delivered without a completion action");
        SaveImage(output.Window, "issue-5-notifications.png");
        await Task.Delay(10500);
        Assert(output.VisibleEvents.Count == 1 && output.Window.IsVisible, "simple signal stays visible after ten seconds");
        reminders.ToggleCommand.Execute(reminders.Signals.Single());
        await UntilAsync(() => !reminders.IsBusy && !model.IsBusy && output.VisibleEvents.Count == 0);
        Assert(!service.ListSignals().Single().Enabled, "disable saves the schedule and removes its native card");
        reminders.DeleteCommand.Execute(reminders.Signals.Single()); await IdleAsync();
        Assert(((FrameworkElement)main.FindName("DeleteOverlay")).IsVisible && service.ListSignals().Count == 1, "deletion requires confirmation inside the main window");
        SaveImage(main, "issue-5-delete-confirmation.png");
        main.CancelDeleteCommand.Execute(null);
        Assert(service.ListSignals().Count == 1, "cancel deletion preserves the signal");
        service.DeleteTask(task.Id);
        reminders.DeleteCommand.Execute(reminders.Signals.Single());
        main.ConfirmDeleteCommand.Execute(null);
        await UntilAsync(() => service.ListSignals().Count == 0 && !model.IsBusy && !reminders.IsBusy);
        await IdleAsync();
        main.Close();
        service = new TaskService(databasePath, clock);
        model = new MainViewModel(service); main = new MainWindow(model); main.Show();
        await UntilAsync(() => !model.IsBusy && model.HasDeleted);
        model.RestoreDeletedCommand.Execute(null);
        await UntilAsync(() => !model.IsBusy && service.ListSignals().Count == 1);
        await IdleAsync();
        Assert(service.ListSignals().Single().Id == id && !service.ListSignals().Single().Enabled && service.ListAll().Count == 0 && ((FrameworkElement)main.FindName("RemindersHost")).IsVisible, "restart restore returns only last signal, disabled, and navigates to its shared list");
        main.Close();
    }
}
