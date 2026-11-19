using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using Notifs.App;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static partial class UiScenario
{
    private static async Task RunNotificationsAsync(string databasePath)
    {
        var service = new TaskService(databasePath);
        service.Initialize();
        var model = new MainViewModel(service);
        var main = new MainWindow(model);
        main.Show();
        await UntilAsync(() => !model.IsBusy);
        main.SettingsCommand.Execute(null);
        await IdleAsync();
        var settings = (SettingsView)((ContentControl)main.FindName("SettingsHost")).Content;
        var settingsModel = (SettingsViewModel)settings.DataContext;
        settingsModel.SnoozeMinutes = "0";
        settingsModel.SaveCommand.Execute(null);
        Assert(settingsModel.HasError && service.ReadNotificationSettings().SnoozeMinutes == 30, "invalid settings do not change saved defaults");
        settingsModel.SnoozeMinutes = "15"; settingsModel.Sound = false;
        settingsModel.SaveCommand.Execute(null);
        await UntilAsync(() => !settingsModel.IsBusy);
        Assert(service.ReadNotificationSettings() == new NotificationSettings(false, 15), "settings save sound and ordinary snooze atomically");
        await SizeAsync(main, 1010, 1060);
        SaveImage(main, "issue-4-settings.png");
        var input = new TextBox { Text = "Печать: " };
        var other = new Window { Title = "Notifs QA — другое приложение", Content = input, Width = 400, Height = 200 };
        other.Show(); other.Activate(); input.Focus();
        await IdleAsync();
        using var output = new NativeNotificationOutput();
        var delivery = new NotificationDelivery(output);
        output.HideRequested = delivery.Hide; output.HideAllRequested = delivery.HideAll;
        var a = new NotificationEvent(Guid.NewGuid(), NotificationKind.Signal, "a", [new(new("signal", Guid.NewGuid()), "A · Размяться", "Небольшой перерыв пойдёт на пользу.")]);
        var b = a with { Id = Guid.NewGuid(), SourceId = "b", Subjects = [a.Subjects[0] with { Title = "B · Личное напоминание" }], Actions = [new("complete", "Готово")] };
        var c = b with { Id = Guid.NewGuid(), SourceId = "c", Subjects = [a.Subjects[0] with { Title = "C · Последняя карточка" }] };
        delivery.Publish(a, false); delivery.Publish(b, false); delivery.Publish(c, false);
        await IdleAsync();
        Assert(other.IsActive && ReferenceEquals(Keyboard.FocusedElement, input), "native arrival of three cards preserves the foreground window and typing focus");
        Assert(output.Window is { ShowActivated: false, ShowInTaskbar: false, IsVisible: true } && output.VisibleEvents.Select(x => x.Id).SequenceEqual(new[] { b.Id, c.Id }), "native stack shows B above C without activation or taskbar entry");
        await Task.Delay(11000);
        Assert(output.Window.IsVisible && output.VisibleEvents.Count == 2, "native cards remain visible for more than ten seconds");
        SaveImage(output.Window, "notifications-native.png");
        delivery.Hide(b.Id);
        await IdleAsync();
        Assert(output.VisibleEvents.Single().Id == c.Id, "closing upper native card keeps remaining card at bottom");
        delivery.HideAll();
        Assert(!output.Window.IsVisible, "empty native stack hides its hide-all bar and window");
        using var runtime = new NotificationRuntime(service, model, main, output);
        var now = DateTime.Now.AddMinutes(-1);
        var task = service.SaveEditor(null, "Настоящее личное событие", "", null, null, ImportanceRank.B,
            new(DateOnly.FromDateTime(now), TimeOnly.FromDateTime(now), 60));
        await runtime.RunOnceAsync();
        await IdleAsync();
        Assert(output.VisibleEvents.Single().Subjects.Single().Key.Id == task.Id, "runtime routes saved personal source to native output");
        var complete = Descendants<Button>(output.Window).Single(x => Equals(x.Content, "Готово"));
        complete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => service.List(true).Any(x => x.Id == task.Id) && output.VisibleEvents.Count == 0);
        Assert(service.List(true).Single().Title == task.Title, "native complete uses application command and removes related cards");
        other.Close(); main.Close();
    }
}
