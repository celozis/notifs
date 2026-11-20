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
    private static async Task RunSummaryAsync(string databasePath)
    {
        var clock = new SummaryClock(new DateTimeOffset(2026, 11, 20, 7, 0, 0, TimeSpan.Zero));
        var service = new TaskService(databasePath, clock);
        service.Initialize();
        var a = service.Create("Закончить практическую работу", "", new(new(2026, 11, 20), new(20, 0)), 420, ImportanceRank.S);
        var b = service.Create("Отправить домашнюю работу", "", new(new(2026, 11, 20), new(14, 0)), null, ImportanceRank.A);
        service.Create("Пройти курс по C#", "", new(new(2026, 12, 20), new(12, 0)), 1200);
        var model = new MainViewModel(service);
        var main = new MainWindow(model);
        main.Show();
        await UntilAsync(() => !model.IsBusy);
        await SizeAsync(main, 1010, 930);
        main.SettingsCommand.Execute(null);
        await IdleAsync();
        var view = (SettingsView)((ContentControl)main.FindName("SettingsHost")).Content;
        var settings = (SettingsViewModel)view.DataContext;
        Assert(settings.SummaryEnabled && settings.SummaryMinutes == "180" && settings.SummaryTaskCount == "3", "summary settings use approved enabled defaults");
        settings.SummaryFirstDate = "20.11.2026";
        settings.SummaryFirstTime = "12:01";
        settings.SaveCommand.Execute(null);
        await UntilAsync(() => !settings.IsBusy);
        SaveImage(main, "issue-7-settings.png");
        settings.SummaryTaskCount = "11"; settings.SnoozeMinutes = "45";
        settings.SaveCommand.Execute(null); await IdleAsync();
        Assert(settings.HasError && ReferenceEquals(Keyboard.FocusedElement, view.FindName("SummaryCountInput")) && service.ReadNotificationSettings().SnoozeMinutes == 30,
            "invalid summary count focuses its field and preserves other saved sections");
        settings.SummaryTaskCount = "3"; settings.SummaryMinutes = "0";
        settings.SaveCommand.Execute(null); await IdleAsync();
        Assert(settings.HasError && ReferenceEquals(Keyboard.FocusedElement, view.FindName("SummaryMinutesInput")), "invalid summary interval returns keyboard focus");
        settings.SummaryMinutes = "180"; settings.SummaryFirstTime = "25:99";
        settings.SaveCommand.Execute(null); await IdleAsync();
        Assert(settings.HasError && ReferenceEquals(Keyboard.FocusedElement, view.FindName("SummaryTimeInput")), "invalid summary time retains input and focuses its field");
        settings.SummaryFirstTime = "12:01";
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_summary BEFORE UPDATE ON summary_settings BEGIN SELECT RAISE(ABORT,'blocked'); END"; command.ExecuteNonQuery();
        }
        settings.SaveCommand.Execute(null); await UntilAsync(() => !settings.IsBusy);
        Assert(settings.HasError && settings.SnoozeMinutes == "45" && service.ReadNotificationSettings().SnoozeMinutes == 30, "SQLite failure keeps entered settings and rolls back both sections");
        await SizeAsync(main, 320, 930); SaveImage(main, "issue-7-error-320.png");
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TRIGGER fail_summary"; command.ExecuteNonQuery();
        }
        settings.SaveCommand.Execute(null); await UntilAsync(() => !settings.IsBusy);
        foreach (var width in new[] { 768, 375, 320 })
        {
            await SizeAsync(main, width, 930);
            ((FrameworkElement)view.FindName("SummaryTimeInput")).BringIntoView(); await IdleAsync();
            foreach (var scroll in Descendants<ScrollViewer>(view).Where(x => x.IsVisible))
                Assert(scroll.ExtentWidth <= scroll.ViewportWidth + 1, "settings have no horizontal overflow at " + width);
            Assert(Descendants<Button>(view).Single(x => ReferenceEquals(x.Command, settings.SaveCommand)).IsVisible, "save settings remains accessible at " + width);
            SaveImage(main, $"issue-7-settings-{width}.png");
        }
        await SizeAsync(main, 1010, 930);
        var input = new TextBox { Text = "Продолжаю печатать" };
        var other = new Window { Title = "Notifs QA — фокус сводки", Content = input, Width = 400, Height = 200 };
        other.Show(); other.Activate(); input.Focus(); await IdleAsync();
        var foreground = GetForegroundWindow();
        using var output = new NativeNotificationOutput();
        using var runtime = new NotificationRuntime(service, model, main, output);
        clock.Advance(TimeSpan.FromMinutes(1));
        await runtime.RunOnceAsync();
        await IdleAsync();
        Assert(output.VisibleEvents.Single().Kind == NotificationKind.Summary && output.VisibleEvents.Single().Subjects.Count == 3, "runtime delivers summary into the native shared stack");
        Assert(GetForegroundWindow() == foreground && ReferenceEquals(Keyboard.FocusedElement, input), "native summary does not steal foreground or text focus");
        Assert(Descendants<Button>(output.Window).Count(x => Equals(x.Content, "Открыть задачи")) == 1, "native summary provides one full-list action");
        SaveImage(output.Window, "issue-7-summary.png");
        service.Complete(a.Id);
        await IdleAsync();
        Assert(output.VisibleEvents.Single().Subjects.Count == 2, "completion removes only its summary row");
        service.DeleteTask(b.Id);
        await IdleAsync();
        Assert(output.VisibleEvents.Single().Subjects.Count == 1, "deletion preserves other summary rows");
        await ExecuteAsync(model.CompletedCommand, model);
        var openTasks = Descendants<Button>(output.Window).Single(x => Equals(x.Content, "Открыть задачи"));
        openTasks.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => output.VisibleEvents.Count == 0 && !model.IsBusy);
        Assert(model.IsCompleted && !model.IsHistory && ((FrameworkElement)main.FindName("ListPane")).IsVisible, "open tasks shows full-list page and preserves saved filter without a hidden summary subset");
        foreach (var task in service.List()) service.Complete(task.Id);
        clock.Advance(TimeSpan.FromHours(3)); await runtime.RunOnceAsync();
        Assert(output.VisibleEvents.Count == 0, "empty native result produces no card");
        var longTitle = string.Join(" ", Enumerable.Repeat("Длинное название задачи", 30));
        for (var index = 0; index < 200; index++) service.Create(longTitle + index, "");
        var saved = service.ReadSummarySettings();
        service.SaveSettings(new(false, 45), saved with { TaskCount = 10 });
        clock.Advance(TimeSpan.FromHours(3)); await runtime.RunOnceAsync(); await IdleAsync();
        Assert(output.VisibleEvents.Single().Subjects.Count == 10, "large native result is limited to ten tasks");
        var fullListAction = Descendants<Button>(output.Window).Single(x => Equals(x.Content, "Открыть задачи"));
        var position = fullListAction.TransformToAncestor((Visual)output.Window.Content).Transform(new Point(0, 0));
        Assert(position.Y + fullListAction.ActualHeight <= ((FrameworkElement)output.Window.Content).ActualHeight + 1, "long summary keeps its full-list action outside the scrolling list");
        SaveImage(output.Window, "issue-7-long-summary.png");
        main.SettingsCommand.Execute(null); await IdleAsync();
        var root = (FrameworkElement)main.Content;
        foreach (var scale in new[] { 1.5, 2.0 })
        {
            root.LayoutTransform = new ScaleTransform(scale, scale); await IdleAsync();
            SaveImage(main, $"issue-7-scale-{scale * 100:0}.png");
        }
        root.LayoutTransform = Transform.Identity;
        other.Close();
        main.Close();
        var reopened = new TaskService(databasePath, clock); reopened.Initialize();
        Assert(reopened.ReadSummarySettings().TaskCount == 10 && reopened.PollSummary().Count == 0, "restart keeps summary settings and consumed tick without replaying visible cards");
    }

    private sealed class SummaryClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Summary UI UTC+5", TimeSpan.FromHours(5), "Test", "Test");
        public void Advance(TimeSpan amount) => now += amount;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
