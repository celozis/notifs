using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using Notifs.App;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static class BackgroundUiScenario
{
    public static int Run(Application app, string directory)
    {
        var result = 1;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await RunInSuiteAsync(directory); result = 0; }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { app.Shutdown(); }
        });
        app.Run();
        return result;
    }
    internal static async Task RunInSuiteAsync(string directory)
    {
        var key = "Software\\Notifs.Tests\\" + Guid.NewGuid().ToString("N");
        try { await RunAsync(directory, key); }
        finally { Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }
    private static async Task RunAsync(string directory, string key)
    {
        var database = Path.Combine(directory, "background-ui.db");
        var clock = new Clock(new DateTimeOffset(2026, 11, 20, 7, 0, 0, TimeSpan.Zero));
        var service = new TaskService(database, clock);
        service.Initialize();
        var model = new MainViewModel(service);
        var main = new MainWindow(model);
        main.Show(); await Wait(() => !model.IsBusy);
        await BackgroundProcessScenarios.CheckForegroundAsync(main, Path.Combine(directory, "foreground"));
        var startup = new WindowsAutoStart(key: key);
        var settings = new SettingsViewModel(service, startup);
        var view = new SettingsView(settings);
        main.SettingsCommand.Execute(null);
        ((ContentControl)main.FindName("SettingsHost")).Content = view;
        await Idle();
        var sound = VisualChildren<CheckBox>(view).Single(x => AutomationProperties.GetName(x) == "Короткий негромкий звук");
        sound.Focus(); sound.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); await Idle();
        Check(Keyboard.FocusedElement is CheckBox next && AutomationProperties.GetName(next) == "Показывать сводку", "Tab visits summary before the lower background section");
        settings.StartWithWindows = true;
        Check(!startup.IsEnabled, "startup checkbox remains a draft until explicit save");
        settings.SaveCommand.Execute(null); await Wait(() => !settings.IsBusy);
        Check(!settings.HasError && startup.IsEnabled && new SettingsViewModel(service, new WindowsAutoStart(key: key)).StartWithWindows, "explicit WPF save registers startup and reopening reads Windows state");
        settings.StartWithWindows = false;
        using (var connection = new SqliteConnection($"Data Source={database}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_background BEFORE UPDATE ON summary_settings BEGIN SELECT RAISE(ABORT,'blocked'); END"; command.ExecuteNonQuery();
        }
        settings.SaveCommand.Execute(null); await Wait(() => !settings.IsBusy);
        Check(settings.HasError && !settings.StartWithWindows && startup.IsEnabled, "SQLite failure keeps draft and restores the previous Windows registration");
        using (var connection = new SqliteConnection($"Data Source={database}"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TRIGGER fail_background"; command.ExecuteNonQuery();
        }
        settings.SaveCommand.Execute(null); await Wait(() => !settings.IsBusy);
        Check(!settings.HasError && !startup.IsEnabled, "explicit disable removes startup without changing other saved sections");
        var failed = new SettingsViewModel(service, new DeniedAutoStart(key)) { StartWithWindows = true, SnoozeMinutes = "45" };
        ((ContentControl)main.FindName("SettingsHost")).Content = new SettingsView(failed);
        failed.SaveCommand.Execute(null); await Wait(() => !failed.IsBusy);
        Check(failed.HasError && failed.StartWithWindows && failed.SnoozeMinutes == "45" && !startup.IsEnabled && service.ReadNotificationSettings().SnoozeMinutes == 30,
            "registry denial retains form input, reports failure and preserves other saved sections");
        UiScenario.SaveImage(main, "issue-9-registration-error.png");
        ((ContentControl)main.FindName("SettingsHost")).Content = view;
        foreach (var width in new[] { 1010, 768, 375, 320 })
        {
            main.Width = width + 16; main.Height = 1010;
            await Idle();
            VisualChildren<ScrollViewer>(view).First().ScrollToEnd(); await Idle();
            Check(VisualChildren<ScrollViewer>(view).All(scroll => scroll.ExtentWidth <= scroll.ViewportWidth + 1), "background settings have no horizontal overflow at " + width);
            UiScenario.SaveImage(main, "issue-9-settings-" + width + ".png");
        }
        main.Width = 1028; main.Height = 1010;
        foreach (var scale in new[] { 1.5, 2.0 })
        {
            ((FrameworkElement)main.Content).LayoutTransform = new ScaleTransform(scale, scale);
            await Idle(); VisualChildren<ScrollViewer>(view).First().ScrollToEnd(); await Idle();
            UiScenario.SaveImage(main, $"issue-9-scale-{scale * 100:0}.png");
        }
        ((FrameworkElement)main.Content).LayoutTransform = Transform.Identity;
        main.NewCommand.Execute(null); await Idle();
        var editor = (EditorViewModel)((EditorView)((ContentControl)main.FindName("EditorHost")).Content).DataContext;
        editor.Title = "Незавершённый черновик перед треем";
        using var output = new NativeNotificationOutput();
        using var runtime = new NotificationRuntime(service, model, main, output);
        var exited = false;
        using var lifetime = new DesktopLifetime(main, runtime, () => exited = true);
        main.Close(); await Idle();
        Check(!main.IsVisible && !exited && service.ReadDraft(null)?.Title == editor.Title, "title-bar close hides main window and preserves the editor draft without exit");
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.Local);
        service.SaveEditor(null, "Личное напоминание из трея", "", null, null, ImportanceRank.A, new(DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime)));
        await runtime.RunOnceAsync();
        Check(!main.IsVisible && output.VisibleEvents.Count == 1 && output.Window.IsVisible, "hidden main window still receives a real native personal reminder");
        UiScenario.SaveImage(output.Window, "issue-9-background-notification.png");
        var notificationId = output.VisibleEvents.Single().Id;
        lifetime.Show(); await Idle();
        Check(main.IsVisible && output.VisibleEvents.Single().Id == notificationId && ReferenceEquals(((EditorView)((ContentControl)main.FindName("EditorHost")).Content).DataContext, editor), "tray opening preserves the same editor and native card");
        var task = service.List().Single(); service.SnoozePersonal(task.Id);
        await Idle();
        clock.Advance(TimeSpan.FromMinutes(30)); await runtime.RunOnceAsync();
        Check(output.VisibleEvents.Count == 1, "snoozed personal notification returns while scheduler is running");
        service.SnoozePersonal(task.Id);
        var snoozed = service.ReadPersonal(task.Id)!.SnoozedUntil;
        var secondTask = service.SaveEditor(null, "Вторая видимая карточка перед выходом", "", null, null, ImportanceRank.B, new(DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime)));
        await Idle(); await runtime.RunOnceAsync();
        Check(output.VisibleEvents.Count == 1, "exit starts with a visible native stack and a separately snoozed task");
        using (var lockConnection = new SqliteConnection($"Data Source={database}"))
        {
            lockConnection.Open(); using var command = lockConnection.CreateCommand(); command.CommandText = "BEGIN IMMEDIATE"; command.ExecuteNonQuery();
            var snooze = VisualChildren<Button>(output.Window).Single(x => x.Content is string text && text.StartsWith("Отложить на"));
            snooze.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var exit = lifetime.ExitAsync();
            await Task.Delay(100);
            Check(!exit.IsCompleted && !exited, "exit waits for a notification action blocked by a real SQLite transaction");
            command.CommandText = "COMMIT"; command.ExecuteNonQuery();
            Check(await exit && exited && output.VisibleEvents.Count == 0, "explicit exit closes main window and native stack after pending actions commit");
        }
        var reopened = new TaskService(database, clock); reopened.Initialize();
        Check(reopened.List().Any(x => x.Id == task.Id) && reopened.ReadPersonal(task.Id)!.SnoozedUntil == snoozed && reopened.ReadDraft(null)?.Title == editor.Title,
            "full exit preserves tasks, schedules, snooze and drafts across SQLite reopen");
        Check(reopened.ReadPersonal(secondTask.Id)!.SnoozedUntil == clock.GetUtcNow().AddMinutes(30), "notification snooze immediately before exit persists across reopen");
        clock.Advance(TimeSpan.FromHours(1)); await runtime.RunOnceAsync();
        Check(output.VisibleEvents.Count == 0, "disposed scheduler cannot deliver after explicit exit");
    }
    private sealed class DeniedAutoStart(string key) : WindowsAutoStart(key: key)
    { public override void WriteCommand(string? command) => throw new UnauthorizedAccessException("denied registry"); }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; public void Advance(TimeSpan duration) => now += duration; }
    private static Task Idle() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static async Task Wait(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException(); await Task.Delay(25); }
        await Idle();
    }
    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); if (child is T found) yield return found; foreach (var nested in VisualChildren<T>(child)) yield return nested; }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
}
