using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Notifs.App;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static partial class UiScenario
{
    private static async Task RunCalendarAsync(string path)
    {
        var clock = new CalendarScenarios.CalendarClock();
        var service = new TaskService(path, clock); service.Initialize();
        var span = service.Create("Навести порядок на рабочем столе", "Расчётный промежуток", estimateMinutes: 4320, rank: ImportanceRank.C);
        clock.Now = new(2026, 11, 18, 7, 0, 0, TimeSpan.Zero);
        var first = service.Create("Закончить практическую работу", "План работы", new(new(2026, 11, 18), new(20, 0)), 420, ImportanceRank.S);
        service.Create("Отправить домашнюю работу", "", new(new(2026, 11, 18), new(14, 0)), 40, ImportanceRank.A);
        service.Create("Сдать отчёт по проекту", "", new(new(2026, 11, 24), new(16, 0)), 240, ImportanceRank.A);
        var completed = service.Create("Записаться на следующую неделю", "", new(new(2026, 11, 18)), 15); service.Complete(completed.Id);
        var model = new MainViewModel(service); var main = new MainWindow(model); main.Show(); await UntilAsync(() => !model.IsBusy);
        await SizeAsync(main, 1010, 1180); await ExecuteAsync(main.CalendarCommand, model);
        Assert(model.IsCalendar && model.Tasks.Count == 4, "calendar week shows selected current day including completed tasks");
        Assert(model.Tasks.Single(x => x.Item.Id == first.Id).CalendarReasons == "Создана · Дедлайн", "calendar list displays all matching reasons on one row");
        SaveImage(main, "issue-8-week.png");
        await model.ChangeCalendarAsync(model.Calendar with { SelectedDay = new(2026, 11, 16) });
        Assert(model.Tasks.Single().Item.Id == span.Id, "WPF Monday includes Saturday's 72-hour task");
        await model.ChangeCalendarAsync(model.Calendar with { SelectedDay = new(2026, 11, 18), IsMonth = true }); await IdleAsync();
        var month = (ScrollViewer)main.FindName("MonthHost");
        Assert(month.IsVisible && !((FrameworkElement)main.FindName("ContentGrid")).IsVisible && Descendants<Button>(month).Count(x => x.Tag is DateOnly) == 42, "full month replaces list with all six rows and visible neighbouring dates");
        Assert(Descendants<TextBlock>(month).Any(x => x.Text == "Ещё 1"), "month limits mini rows to three and reports the remainder");
        month.ScrollToEnd(); await IdleAsync(); Assert(((FrameworkElement)main.FindName("MonthWeekdays")).IsVisible, "weekday headings remain visible while month rows scroll"); month.ScrollToTop(); await IdleAsync();
        SaveImage(main, "issue-8-month.png");
        var selected = Descendants<Button>(month).Single(x => x.Tag is DateOnly day && day == new DateOnly(2026, 11, 18)); selected.Focus();
        await main.ShowCalendarDayAsync(new(2026, 11, 18)); await IdleAsync();
        Assert(((FrameworkElement)main.FindName("TaskPanelOverlay")).IsVisible && !((FrameworkElement)main.FindName("Navigation")).IsEnabled, "day panel stays in main window and traps background interaction");
        SaveImage(main, "issue-8-day.png");
        var escape = main.InputBindings.OfType<KeyBinding>().Single(x => x.Key == Key.Escape); escape.Command.Execute(null); await IdleAsync();
        Assert(model.Calendar.IsMonth && Keyboard.FocusedElement is Button { Tag: DateOnly }, "Escape returns focus to month day");
        await main.ShowCalendarDayAsync(new(2026, 11, 18)); await IdleAsync();
        var host = (ContentControl)main.FindName("TaskPanelHost");
        Descendants<Button>(host).Single(x => Equals(x.Content, "Перейти к этому дню")).Command.Execute(null); await UntilAsync(() => !model.IsBusy); await IdleAsync();
        Assert(!model.Calendar.IsMonth && model.Calendar.SelectedDay == new DateOnly(2026, 11, 18), "go to day restores its week and selected day");
        model.Selected = model.Tasks.Single(x => x.Item.Id == first.Id); main.EditCommand.Execute(null); await IdleAsync();
        var editor = (EditorViewModel)((EditorView)((ContentControl)main.FindName("EditorHost")).Content).DataContext;
        editor.Title = "Изменённая практическая работа"; editor.SaveCommand.Execute(null); await UntilAsync(() => !editor.IsBusy && !model.IsBusy); await IdleAsync();
        Assert(model.IsCalendar && model.Tasks.Any(x => x.Title == "Изменённая практическая работа"), "editing from calendar saves and returns to same selected period");
        await model.ChangeCalendarAsync(model.Calendar with { IsMonth = true, Month = new(2027, 1, 1) });
        await model.ApplySettingsAsync(new(Status: TaskStatusFilter.Completed, Sort: TaskSort.Rank));
        var savedState = model.Calendar;
        main.Close();
        model = new(new TaskService(path, clock)); main = new(model); main.Show(); await UntilAsync(() => !model.IsBusy); await ExecuteAsync(main.CalendarCommand, model);
        Assert(model.Calendar == savedState && model.IsCompleted && model.Settings.Sort == TaskSort.Rank, "restart restores month, mode, period and independent calendar settings");
        await model.ApplySettingsAsync(TaskListSettings.Default(TaskSection.Calendar)); Assert(model.Calendar == savedState, "filter reset does not change calendar period");
        await SizeAsync(main, 1010, 1180); SaveImage(main, "issue-8-empty-month.png");
        var today = Descendants<Button>((ContentControl)main.FindName("CalendarHeader")).Single(x => Equals(x.Content, "Сегодня")); today.Command.Execute(null); await UntilAsync(() => !model.IsBusy); await IdleAsync();
        Assert(!model.Calendar.IsMonth && model.Calendar.SelectedDay == service.Today, "Today explicitly restores current day and week");
        for (var i = 0; i < 200; i++) service.Create("Длинное название " + i + string.Concat(Enumerable.Repeat(" очень длинное название", 10)), "");
        await model.LoadAsync(reapplyFilter: true);
        foreach (var width in new[] { 768, 375, 320 })
        {
            await SizeAsync(main, width, 930); SaveImage(main, $"issue-8-week-{width}.png");
            await model.ChangeCalendarAsync(model.Calendar with { IsMonth = true });
            Assert(Descendants<ScrollViewer>((FrameworkElement)main.FindName("BrowseView")).Where(x => x.IsVisible).All(x => x.ExtentWidth <= x.ViewportWidth + 1), "calendar has no horizontal overflow at " + width);
            SaveImage(main, $"issue-8-month-{width}.png");
            await main.ShowCalendarDayAsync(service.Today); await IdleAsync(); SaveImage(main, $"issue-8-day-{width}.png"); escape = main.InputBindings.OfType<KeyBinding>().Single(x => x.Key == Key.Escape); escape.Command.Execute(null); await IdleAsync();
            await model.ChangeCalendarAsync(model.Calendar with { IsMonth = false });
        }
        await SizeAsync(main, 1010, 1180); await model.ChangeCalendarAsync(model.Calendar with { IsMonth = true });
        var root = (FrameworkElement)main.Content;
        foreach (var scale in new[] { 1.5, 2.0 }) { root.LayoutTransform = new ScaleTransform(scale, scale); await IdleAsync(); SaveImage(main, $"issue-8-scale-{scale * 100:0}.png"); }
        root.LayoutTransform = Transform.Identity;
        await IdleAsync();
        await SizeAsync(main, 1010, 420); await IdleAsync(); Assert(((ScrollViewer)main.FindName("MonthHost")).ScrollableHeight > 0, "short month scrolls to its last row");
        await SizeAsync(main, 320, 420); await IdleAsync();
        Assert(((ScrollViewer)main.FindName("MonthHost")).ActualHeight > 0 && ((ScrollViewer)main.FindName("BrowseScroll")).ScrollableHeight > 0, "narrow minimum-height window scrolls workspace to reach the calendar");
        SaveImage(main, "issue-8-month-320-low.png");
        await ExecuteAsync(model.ActiveCommand, model); Assert(!model.IsCalendar && model.IsAll, "My tasks removes only calendar period and restores its own settings");
        main.Close();
    }
}
