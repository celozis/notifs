using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using Notifs.App;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static partial class UiScenario
{
    private static async Task RunTaskListsAsync(string path)
    {
        var service = new TaskService(path); service.Initialize();
        var a = service.Create("Высокий ранг", "", estimateMinutes: 120, rank: ImportanceRank.S);
        service.Create("Без оценки", "");
        var model = new MainViewModel(service); var main = new MainWindow(model); main.Show();
        await UntilAsync(() => !model.IsBusy); await SizeAsync(main, 1010, 1060);
        ((Button)main.FindName("OpenFilters")).Focus(); main.FiltersCommand.Execute(null); await IdleAsync();
        var view = (TaskFiltersView)((ContentControl)main.FindName("TaskPanelHost")).Content;
        Assert(Keyboard.FocusedElement is ComboBox && !((FrameworkElement)main.FindName("Workspace")).IsEnabled, "filters receive focus and disable background navigation");
        ComboBox Choice(string name) => Descendants<ComboBox>(view).Single(x => AutomationProperties.GetName(x) == name);
        Choice("Порядок").SelectedIndex = 2; Choice("Направление").SelectedIndex = 1;
        SaveImage(main, "issue-13-filters.png");
        Descendants<Button>(view).Single(x => Equals(x.Content, "Применить")).RaiseEvent(new(Button.ClickEvent)); await UntilAsync(() => !((FrameworkElement)main.FindName("TaskPanelOverlay")).IsVisible);
        Assert(model.Settings.Sort == TaskSort.Estimate, "apply persists manual order");
        Assert(ReferenceEquals(Keyboard.FocusedElement, main.FindName("OpenFilters")), "apply restores focus: " + Keyboard.FocusedElement);
        main.FiltersCommand.Execute(null); await IdleAsync();
        view = (TaskFiltersView)((ContentControl)main.FindName("TaskPanelHost")).Content;
        Descendants<Button>(view).Single(x => Equals(x.Content, "Сбросить")).RaiseEvent(new(Button.ClickEvent));
        Descendants<Button>(view).Single(x => AutomationProperties.GetName(x) == "Закрыть фильтры").RaiseEvent(new(Button.ClickEvent));
        Assert(model.Settings.Sort == TaskSort.Estimate, "reset then close does not change applied order");
        main.FiltersCommand.Execute(null); await IdleAsync();
        view = (TaskFiltersView)((ContentControl)main.FindName("TaskPanelHost")).Content;
        var input = Descendants<TextBox>(view).Single(x => AutomationProperties.GetName(x) == "Дедлайн с"); input.Text = "31.02.2026";
        Descendants<Button>(view).Single(x => Equals(x.Content, "Применить")).RaiseEvent(new(Button.ClickEvent)); await IdleAsync();
        Assert(ReferenceEquals(Keyboard.FocusedElement, input) && input.Text == "31.02.2026", "invalid date retains draft and focuses the field");
        input.Text = "20.11.2026";
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TRIGGER fail_filter BEFORE UPDATE ON task_list_settings BEGIN SELECT RAISE(ABORT,'blocked'); END"; command.ExecuteNonQuery();
        }
        Descendants<Button>(view).Single(x => Equals(x.Content, "Применить")).RaiseEvent(new(Button.ClickEvent)); await UntilAsync(() => !view.IsBusy);
        Assert(((FrameworkElement)main.FindName("TaskPanelOverlay")).IsVisible && input.Text == "20.11.2026" && service.ReadTaskListSettings(TaskSection.Tasks).DeadlineFrom == null, "failed SQLite apply keeps panel input and previous applied values");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TRIGGER fail_filter"; command.ExecuteNonQuery();
        }
        foreach (var width in new[] { 768, 375, 320 })
        {
            await SizeAsync(main, width, 930);
            Assert(Descendants<ScrollViewer>(view).All(x => x.ExtentWidth <= x.ViewportWidth + 1), "filter panel does not overflow at " + width);
            SaveImage(main, $"issue-13-filters-{width}.png");
        }
        Descendants<Button>(view).Single(x => AutomationProperties.GetName(x) == "Закрыть фильтры").RaiseEvent(new(Button.ClickEvent));
        await ExecuteAsync(model.HistoryCommand, model);
        Assert(model.IsCompleted && model.Settings.Sort == TaskSort.Urgency, "history has independent completed defaults");
        await ExecuteAsync(model.ActiveCommand, model); Assert(model.Settings.Sort == TaskSort.Estimate, "returning restores task settings");
        main.Close();
    }
}
