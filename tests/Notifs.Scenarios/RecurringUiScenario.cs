using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using Notifs.App;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static partial class UiScenario
{
    private static async Task RunRecurringAsync(string databasePath)
    {
        var clock = new SignalScenarios.Clock(new(2026, 11, 18, 7, 0, 0, TimeSpan.Zero));
        var service = new TaskService(databasePath, clock);
        var model = new MainViewModel(service);
        var main = new MainWindow(model); main.Show();
        await UntilAsync(() => !model.IsBusy); await SizeAsync(main, 1010, 930);
        await main.OpenRemindersAsync(); await IdleAsync();
        var view = (RemindersView)((ContentControl)main.FindName("RemindersHost")).Content;
        var reminders = (RemindersViewModel)view.DataContext;
        reminders.NewCommand.Execute(null); await IdleAsync();
        reminders.Title = "Сменить постельное бельё";
        reminders.ModeIndex = 1; reminders.RepeatIndex = 2;
        reminders.FirstDate = "18.11.2026"; reminders.FirstTime = "15:00";
        reminders.IntervalValue = "14"; reminders.IntervalUnitIndex = 0; reminders.RepeatMinutes = "60";
        reminders.SaveCommand.Execute(null);
        await UntilAsync(() => !reminders.IsBusy && !model.IsBusy);
        Assert(reminders.IsList && service.ListSignals().Single().RequiresCompletion && service.ListSignals().Single().Schedule.Repeat == SignalRepeat.AfterCompletion,
            "shared real WPF form saves confirmed recurring work with fourteen-day interval");
        Assert(!Descendants<Button>(view).Any(x => x.IsVisible && Equals(x.Content, "Готово")), "future recurring work offers no early completion in the real window");
        SaveImage(main, "issue-6-reminders.png");
        var row = reminders.Signals.Single();
        reminders.EditCommand.Execute(row); await IdleAsync();
        SaveImage(main, "issue-6-editor.png");
        foreach (var scale in new[] { 1.5, 2.0 })
        {
            ((FrameworkElement)main.Content).LayoutTransform = new ScaleTransform(scale, scale); await IdleAsync();
            foreach (var scroll in Descendants<ScrollViewer>(view).Where(x => x.IsVisible))
                Assert(scroll.ExtentWidth <= scroll.ViewportWidth + 1, "confirmed form stays within layout at simulated scale " + scale);
            SaveImage(main, $"issue-6-scale-{scale * 100:0}.png");
        }
        ((FrameworkElement)main.Content).LayoutTransform = Transform.Identity; await IdleAsync();
        foreach (var width in new[] { 768, 375, 320 })
        {
            await SizeAsync(main, width, 930);
            foreach (var scroll in Descendants<ScrollViewer>(view).Where(x => x.IsVisible))
                Assert(scroll.ExtentWidth <= scroll.ViewportWidth + 1, "confirmed reminder form has no horizontal overflow at " + width);
            SaveImage(main, $"issue-6-editor-{width}.png");
        }
        reminders.IntervalValue = "0"; reminders.SaveCommand.Execute(null); await IdleAsync();
        Assert(reminders.IsEditing && reminders.HasError && reminders.IntervalValue == "0" && ReferenceEquals(Keyboard.FocusedElement, view.FindName("IntervalInput")),
            "zero interval retains form and focuses invalid field");
        SaveImage(main, "issue-6-invalid-320.png");
        reminders.IntervalValue = "14"; reminders.RepeatMinutes = "0"; reminders.SaveCommand.Execute(null);
        Assert(reminders.Message.Contains("Повтор до выполнения") && service.ListSignals().Single().RepeatMinutes == 60, "zero notification frequency is rejected without changing saved schedule");
        reminders.RepeatMinutes = "60";
        await SizeAsync(main, 1010, 930);
        reminders.CancelCommand.Execute(null); await IdleAsync();
        using var output = new NativeNotificationOutput();
        using var runtime = new NotificationRuntime(service, model, main, output);
        var focusBefore = Keyboard.FocusedElement;
        clock.Advance(TimeSpan.FromHours(3)); await runtime.RunOnceAsync(); await Task.Delay(250);
        Assert(output.VisibleEvents.Single().Kind == NotificationKind.Recurring && row.HasCurrent && ReferenceEquals(Keyboard.FocusedElement, focusBefore),
            "real native confirmed card arrives without stealing main-window focus and list receives current debt");
        SaveImage(main, "issue-6-debt.png"); SaveImage(output.Window, "issue-6-notifications.png");
        foreach (var width in new[] { 375, 320 })
        {
            await SizeAsync(main, width, 930);
            Assert(Descendants<Button>(view).Any(x => x.IsVisible && Equals(x.Content, "Готово")), "current work completion remains visible in narrow list " + width);
            foreach (var scroll in Descendants<ScrollViewer>(view).Where(x => x.IsVisible))
                Assert(scroll.ExtentWidth <= scroll.ViewportWidth + 1, "current-work list has no horizontal overflow at " + width);
            SaveImage(main, $"issue-6-debt-{width}.png");
        }
        await SizeAsync(main, 1010, 930);
        Descendants<Button>(output.Window).Single(x => Equals(x.Content, "×")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(service.ListSignals().Single().Current is not null && output.VisibleEvents.Count == 0, "native close hides card without completing recurring work");
        clock.Advance(TimeSpan.FromHours(1)); await runtime.RunOnceAsync();
        Descendants<Button>(output.Window).Single(x => Equals(x.Content, "Отложить на 30 мин")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => service.ListSignals().Single().SnoozedUntil is not null && output.VisibleEvents.Count == 0);
        clock.Advance(TimeSpan.FromMinutes(30)); await runtime.RunOnceAsync();
        var oldEvent = output.VisibleEvents.Single();
        Descendants<Button>(output.Window).Single(x => Equals(x.Content, "Готово")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => service.ListSignals().Single().History.Count == 1 && output.VisibleEvents.Count == 0);
        await output.ActionRequested!(oldEvent, oldEvent.Subjects[0], "complete");
        Assert(service.ListSignals().Single().History.Count == 1 && !row.HasCurrent && row.HasHistory, "native completion clears only this debt, updates list and repeated action creates no duplicate history");
        clock.Advance(TimeSpan.FromDays(14)); await runtime.RunOnceAsync();
        reminders.EditCommand.Execute(row); await IdleAsync();
        reminders.ModeIndex = 0; reminders.RepeatIndex = 1; reminders.Minute = "25";
        reminders.Description = "Сохранить введённый текст";
        var conversionInitiator = Keyboard.FocusedElement;
        reminders.SaveCommand.Execute(null); await UntilAsync(() => !reminders.IsBusy); await IdleAsync();
        Assert(reminders.HasModeConfirmation && ReferenceEquals(Keyboard.FocusedElement, view.FindName("KeepMode")) && !main.NewCommand.CanExecute(null), "mode conversion opens focused confirmation and blocks background navigation");
        SaveImage(main, "issue-6-mode-confirmation.png");
        main.InputBindings.OfType<KeyBinding>().Single(x => x.Key == Key.Escape).Command.Execute(null); await IdleAsync();
        Assert(!reminders.HasModeConfirmation && reminders.IsEditing && reminders.Description.Contains("введённый") && service.ListSignals().Single().RequiresCompletion,
            "Escape declines mode conversion, preserving saved rule and entered form");
        Assert(ReferenceEquals(Keyboard.FocusedElement, conversionInitiator), "canceling mode confirmation restores focus to the initiating field");
        reminders.SaveCommand.Execute(null); await UntilAsync(() => !reminders.IsBusy); await IdleAsync();
        reminders.ConfirmModeCommand.Execute(null); await UntilAsync(() => !reminders.IsBusy && !model.IsBusy && reminders.IsList);
        Assert(!service.ListSignals().Single().RequiresCompletion && service.ListSignals().Single().Current is null && service.ListSignals().Single().History.Count == 1, "explicit confirmation removes current debt and preserves historical completion");
        reminders.EditCommand.Execute(row); reminders.ModeIndex = 1; reminders.RepeatIndex = 2; reminders.IntervalValue = "1"; reminders.IntervalUnitIndex = 1;
        reminders.FirstDate = "18.11.2026"; reminders.FirstTime = "15:00";
        reminders.SaveCommand.Execute(null); await UntilAsync(() => !reminders.IsBusy && !model.IsBusy && reminders.IsList);
        reminders.ToggleCommand.Execute(row); await UntilAsync(() => !reminders.IsBusy && !model.IsBusy);
        Assert(!row.Item.Enabled && row.HasCurrent && Descendants<Button>(view).Any(x => Equals(x.Content, "Готово") && x.IsVisible), "disabled current work remains completable in the list");
        reminders.CompleteCommand.Execute(row); reminders.CompleteCommand.Execute(row);
        await UntilAsync(() => !reminders.IsBusy && !model.IsBusy);
        Assert(service.ListSignals().Single().History.Count == 2 && !row.HasCurrent && !row.Item.Enabled, "window completion while disabled is idempotent and preserves disabled state");
        SaveImage(main, "issue-6-history.png");
        reminders.EditCommand.Execute(row); await IdleAsync();
        reminders.Title = "Ввод переживает ошибку записи";
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_recurring_write BEFORE UPDATE ON signal_reminders BEGIN SELECT RAISE(ABORT,'blocked'); END"; command.ExecuteNonQuery();
        }
        reminders.SaveCommand.Execute(null); await UntilAsync(() => !reminders.IsBusy);
        Assert(reminders.IsEditing && reminders.Title.Contains("ошибку записи") && reminders.HasError && !service.ListSignals().Single().Title.Contains("ошибку записи"), "failed SQLite save keeps complete recurring form and committed data unchanged");
        await SizeAsync(main, 320, 930); SaveImage(main, "issue-6-error-320.png");
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TRIGGER fail_recurring_write"; command.ExecuteNonQuery();
        }
        reminders.Title = string.Join(" ", Enumerable.Repeat("Длинное название подтверждаемого дела", 12));
        SaveImage(main, "issue-6-long-text-320.png");
        reminders.CancelCommand.Execute(null); await SizeAsync(main, 1010, 930);
        reminders.DeleteCommand.Execute(row); await IdleAsync();
        Assert(((TextBlock)main.FindName("DeleteMessage")).Text.Contains("прошлые выполнения"), "schedule deletion explicitly warns about removing current work and calendar history");
        SaveImage(main, "issue-6-delete-confirmation.png");
        main.CancelDeleteCommand.Execute(null); Assert(service.ListSignals().Count == 1, "cancel deletion leaves recurring schedule and history intact");
        reminders.DeleteCommand.Execute(row); main.ConfirmDeleteCommand.Execute(null);
        await UntilAsync(() => !reminders.IsBusy && !model.IsBusy && service.ListSignals().Count == 0);
        main.Close();
        service = new(databasePath, clock); model = new(service); main = new(model); main.Show(); await UntilAsync(() => !model.IsBusy);
        main.RestoreDeletedCommand.Execute(null); await UntilAsync(() => !model.IsBusy && service.ListSignals().Count == 1); await IdleAsync();
        Assert(!service.ListSignals().Single().Enabled && service.ListSignals().Single().History.Count == 2 && service.ListAll().Count == 0 && ((FrameworkElement)main.FindName("RemindersHost")).IsVisible,
            "restart shared restoration returns disabled recurring schedule and history to reminders without ordinary task copies");
        main.Close();
    }
}
