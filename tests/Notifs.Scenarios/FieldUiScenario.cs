using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Notifs.App;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static partial class UiScenario
{
    private static async Task RunFieldsAsync(string path)
    {
        var clock = new FieldClock(new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
        var service = new TaskService(path, clock);
        service.Initialize();
        var practice = service.Create("Закончить практическую работу", "## Остался последний шаг\n\n- Проверить расчёты\n- Оформить выводы\n- Отправить преподавателю", new(new(2026, 11, 18), new(20, 0)), 420, ImportanceRank.S);
        service.Create("Отправить домашнюю работу", "", new(new(2026, 11, 18), new(14, 0)), 40, ImportanceRank.A);
        service.Create("Сдать отчёт по проекту", "", new(new(2026, 11, 24), new(16, 0)), 240, ImportanceRank.A);
        var course = service.Create("Пройти курс по C#", "", new(new(2026, 12, 18), new(18, 0)), 1200);
        service.Create("Навести порядок на рабочем столе", "", null, 4320, ImportanceRank.C);
        service.Create("Помыть пол", "", null, 45, ImportanceRank.D);
        var complete = service.Create("Купить продукты", "");
        service.Complete(complete.Id);
        clock.Set(new(2026, 11, 18, 7, 0, 0, TimeSpan.Zero));
        var model = new MainViewModel(service);
        var main = new MainWindow(model);
        var bindingErrors = new System.Text.StringBuilder();
        var listener = new BindingErrorListener(bindingErrors);
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        main.Show();
        await UntilAsync(() => !model.IsBusy);
        await model.LoadAsync(practice.Id, true);
        await SizeAsync(main, 1010, 1060);
        Assert(model.Selected!.SlackLabel == "1 ч" && model.Selected.EstimateLabel == "7 ч" && model.Selected.AgeLabel == "Создана вчера", "details show clock-based age, full estimate and positive slack");
        Assert(Descendants<RankBadge>(main).Where(x => x.IsVisible).All(x => x.ActualWidth == 26 && x.ActualHeight == 26), "card and detail rank badges are 26 DIP");
        Assert(Descendants<RankBadge>(main).All(x => !x.Focusable && !x.IsTabStop), "decorative rank badges do not add keyboard stops");
        SaveImage(main, "issue-3-tasks.png");

        main.NewCommand.Execute(null);
        await IdleAsync();
        var view = (EditorView)((ContentControl)main.FindName("EditorHost")).Content;
        var editor = (EditorViewModel)view.DataContext;
        Assert(editor.Rank == ImportanceRank.B && editor.DeadlineDate == "" && editor.EstimateMinutes == "", "editor defaults are B and absent optional values");
        SetInput(view, "TitleInput", "Проверить новую форму");
        SetInput(view, "DescriptionInput", "Не терять **описание**");
        SetInput(view, "DeadlineTimeInput", "12:00");
        editor.SaveCommand.Execute(null);
        Assert(editor.HasError && editor.DeadlineTime == "12:00" && editor.Description.Contains("**описание**") && service.ListAll().Count == 7, "time without date reports error without hidden deadline or lost input");
        Assert(ReferenceEquals(Keyboard.FocusedElement, view.FindName("DeadlineDateInput")), "invalid date receives keyboard focus");
        SetInput(view, "DeadlineDateInput", "31.02.2026");
        editor.SaveCommand.Execute(null);
        Assert(editor.HasError && editor.DeadlineDate == "31.02.2026", "invalid partial date remains in form");
        SetInput(view, "DeadlineDateInput", "19.11.2026");
        SetInput(view, "DeadlineTimeInput", "25:00");
        editor.SaveCommand.Execute(null);
        Assert(editor.HasError && editor.DeadlineTime == "25:00", "invalid time remains in form");
        SetInput(view, "DeadlineTimeInput", "");
        foreach (var invalid in new[] { "0", "-1", "много", "1,5" })
        {
            SetInput(view, "EstimateMinutesInput", invalid);
            editor.SaveCommand.Execute(null);
            Assert(editor.HasError && editor.EstimateMinutes == invalid && editor.Title == "Проверить новую форму", "invalid estimate retains all fields: " + invalid);
        }
        SaveImage(main, "issue-3-editor-invalid.png");
        SetInput(view, "EstimateHoursInput", "1");
        SetInput(view, "EstimateMinutesInput", "30");
        SetInput(view, "DeadlineDateInput", "31.12.9999");
        editor.SaveCommand.Execute(null);
        await UntilAsync(() => !editor.IsBusy);
        Assert(editor.HasError && ReferenceEquals(Keyboard.FocusedElement, view.FindName("DeadlineDateInput")), "application validation re-enables form before focusing invalid deadline date");
        SetInput(view, "DeadlineDateInput", "19.11.2026");
        ((ListBox)view.FindName("RankInput")).SelectedItem = ImportanceRank.C;
        Assert(Descendants<RankBadge>(view).Count() == 5, "form displays all five lettered ranks");
        var rankColors = new[] { "#FF6A2C35/#FFFFD4D6", "#FF65451F/#FFFFDB9E", "#FF5D5429/#FFF4E6AC", "#FF49582E/#FFE3EDC0", "#FF2E533D/#FFD0EFD9" };
        Assert(Descendants<RankBadge>(view).OrderBy(x => x.Rank).Select(x => ((SolidColorBrush)x.Background).Color + "/" + ((SolidColorBrush)x.Foreground).Color).SequenceEqual(rankColors), "all five rank fills/text match approved tokens");
        SaveImage(main, "issue-3-editor.png");
        editor.SaveCommand.Execute(null);
        await UntilAsync(() => !editor.IsBusy && !model.IsBusy && model.Tasks.Count == 8);
        var newId = model.Selected!.Item.Id;
        var saved = new TaskService(path, clock).ListAll().Single(x => x.Id == newId);
        Assert(saved.EstimateMinutes == 90 && saved.Rank == ImportanceRank.C && saved.Deadline == new TaskDeadline(new(2026, 11, 19)), "WPF form persists hour/minute conversion, selected rank and date without time");
        Assert(model.Selected.DeadlineLabel.Contains("до конца дня"), "details preserve date-only meaning");

        main.EditCommand.Execute(null);
        await IdleAsync();
        view = (EditorView)((ContentControl)main.FindName("EditorHost")).Content;
        editor = (EditorViewModel)view.DataContext;
        Assert(editor.EstimateHours == "1" && editor.EstimateMinutes == "30" && !editor.IsDirty, "editing restores complete field values without changes");
        SetInput(view, "DeadlineDateInput", "");
        SetInput(view, "EstimateHoursInput", "");
        SetInput(view, "EstimateMinutesInput", "");
        editor.SaveCommand.Execute(null);
        await UntilAsync(() => !editor.IsBusy && !model.IsBusy && model.Selected?.Item.Deadline is null);
        Assert(model.Selected!.Item.EstimateMinutes is null && model.Selected.Item.CreatedAt == saved.CreatedAt && model.Selected.DeadlineLabel == "Без дедлайна" && model.Selected.EstimateLabel == "Без оценки", "clearing fields saves absence without changing creation");

        model.Selected = model.Tasks.Single(x => x.Item.Id == practice.Id);
        main.EditCommand.Execute(null);
        await IdleAsync();
        view = (EditorView)((ContentControl)main.FindName("EditorHost")).Content;
        editor = (EditorViewModel)view.DataContext;
        SetInput(view, "EstimateHoursInput", "8");
        SetInput(view, "EstimateMinutesInput", "0");
        editor.SaveCommand.Execute(null);
        await UntilAsync(() => !editor.IsBusy && !model.IsBusy && model.Selected?.Item.EstimateMinutes == 480);
        Assert(model.Selected!.SlackLabel == "0 мин", "zero slack is shown as zero instead of absent estimate");
        SaveImage(main, "issue-3-zero-slack.png");
        main.EditCommand.Execute(null);
        await IdleAsync();
        view = (EditorView)((ContentControl)main.FindName("EditorHost")).Content;
        editor = (EditorViewModel)view.DataContext;
        SetInput(view, "EstimateHoursInput", "9");
        editor.SaveCommand.Execute(null);
        await UntilAsync(() => !editor.IsBusy && !model.IsBusy && model.Selected?.Item.EstimateMinutes == 540);
        Assert(model.Selected!.SlackLabel == "−1 ч" && model.Selected.HasNegativeSlack && !model.Selected.IsOverdue, "negative slack has a sign and a distinct pre-deadline state");
        SaveImage(main, "issue-3-negative-slack.png");
        var held = model.Tasks.Single(x => x.Item.Id == course.Id);
        var heldPosition = model.Tasks.IndexOf(held);
        model.ToggleCommand.Execute(held);
        await UntilAsync(() => !model.IsBusy);
        clock.Set(new(2026, 11, 18, 15, 1, 0, TimeSpan.Zero));
        model.RefreshTime();
        Assert(model.Tasks.IndexOf(held) == heldPosition && held.IsCompleted, "clock refresh preserves a completed card's held position");
        Assert(model.Selected!.IsOverdue && model.Selected.EstimateLabel == "9 ч", "time crossing deadline refreshes overdue without reducing estimate");
        Assert(model.Tasks.First().Title == "Отправить домашнюю работу", "time refresh reorders other cards by oldest overdue deadline");
        SaveImage(main, "issue-3-overdue.png");
        clock.Set(new(2026, 11, 19, 7, 0, 0, TimeSpan.Zero));
        model.RefreshTime();
        Assert(model.Selected.AgeLabel == "Создана 2 дн. назад", "local date change refreshes task age");
        await ExecuteAsync(model.AllCommand, model);
        Assert(model.Tasks.Skip(6).All(x => x.IsCompleted), "explicit filter switch releases held completion positions");

        foreach (var width in new[] { 768, 375, 320 })
        {
            await SizeAsync(main, width, 1060);
            main.EditCommand.Execute(null);
            await IdleAsync();
            view = (EditorView)((ContentControl)main.FindName("EditorHost")).Content;
            Assert((((Grid)view.FindName("FieldsPanel")).ColumnDefinitions[1].ActualWidth > 0) == (width == 768), "field layout adapts at " + width);
            foreach (var scroll in Descendants<ScrollViewer>(view).Where(x => x.IsVisible))
                Assert(scroll.ExtentWidth <= scroll.ViewportWidth + 1, "new fields do not overflow at " + width);
            SaveImage(main, $"issue-3-editor-{width}.png");
            main.CancelEditorCommand.Execute(null);
        }
        main.Close();
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        Assert(bindingErrors.Length == 0, "new field and rank bindings have no errors: " + bindingErrors);
        await FailedFieldSaveAsync(path, clock);
    }

    private static async Task FailedFieldSaveAsync(string path, TimeProvider clock)
    {
        var unavailable = new TaskService(Path.Combine(path, "unavailable.db"), clock);
        var editor = new EditorViewModel(unavailable, null) { Title = "Не потерять поля", DeadlineDate = "20.11.2026", DeadlineTime = "10:00", EstimateHours = "2", EstimateMinutes = "15", Rank = ImportanceRank.A };
        var signaledSuccess = false;
        editor.Saved += _ => signaledSuccess = true;
        editor.SaveCommand.Execute(null);
        await UntilAsync(() => !editor.IsBusy);
        Assert(editor.HasError && !signaledSuccess && editor.DeadlineDate == "20.11.2026" && editor.DeadlineTime == "10:00" && editor.EstimateHours == "2" && editor.EstimateMinutes == "15" && editor.Rank == ImportanceRank.A,
            "failed SQLite write retains all new fields and never reports success");
    }

    private static void SetInput(EditorView view, string name, string text) => ((TextBox)view.FindName(name)).Text = text;
    private sealed class FieldClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("UI UTC+5", TimeSpan.FromHours(5), "UTC+5", "UTC+5");
        public void Set(DateTimeOffset value) => now = value;
    }
}
