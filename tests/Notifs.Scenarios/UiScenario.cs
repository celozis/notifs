using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Notifs.App;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static partial class UiScenario
{
    public static int Run(Application app, string databasePath, bool fieldsOnly = false, bool draftsOnly = false, bool notificationsOnly = false, bool signalsOnly = false, bool recurringOnly = false)
    {
        var result = 1;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { if (recurringOnly) await RunRecurringAsync(databasePath); else if (signalsOnly) await RunSignalsAsync(databasePath); else if (notificationsOnly) await RunNotificationsAsync(databasePath); else if (draftsOnly) await RunDraftsAsync(databasePath); else if (fieldsOnly) await RunFieldsAsync(databasePath); else { await RunAsync(databasePath); await RunDraftsAsync(databasePath + ".drafts"); await RunNotificationsAsync(databasePath + ".notifications"); await RunSignalsAsync(databasePath + ".signals"); await RunRecurringAsync(databasePath + ".recurring"); } result = 0; Console.WriteLine("PASS all requested WPF scenarios"); }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { app.Shutdown(); }
        });
        app.Run();
        return result;
    }

    private static async Task RunAsync(string databasePath)
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero));
        var service = new TaskService(databasePath, clock);
        var model = new MainViewModel(service);
        var main = new MainWindow(model);
        var bindingErrors = new System.Text.StringBuilder();
        var listener = new BindingErrorListener(bindingErrors);
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        main.Show();
        await UntilAsync(() => !model.IsBusy);
        await model.LoadAsync();
        await SizeAsync(main, 1010, 1060);
        SaveImage(main, "empty.png");
        Assert(model.IsAll && model.IsEmpty, "initial filter is All; empty state is visible");

        main.NewCommand.Execute(null);
        await IdleAsync();
        var view = (EditorView)((ContentControl)main.FindName("EditorHost")).Content;
        var editor = (EditorViewModel)view.DataContext;
        Assert(Application.Current.Windows.Count == 1, "editor stays inside the main WPF window");
        Assert(ReferenceEquals(Keyboard.FocusedElement, view.FindName("TitleInput")), "editor receives keyboard focus");
        const string markdown = "# Небольшой план\n\n- **Купить** продукты\n- *Приготовить* ужин\n\n```text\nпаста + томаты + базилик\n```";
        ((TextBox)view.FindName("DescriptionInput")).Text = markdown;
        ((TextBox)view.FindName("TitleInput")).Text = "   ";
        editor.SaveCommand.Execute(null);
        Assert(editor.HasError && editor.Description == markdown && service.ListAll().Count == 0, "invalid submission retains description and writes nothing");
        SaveImage(main, "editor-invalid.png");
        ((TextBox)view.FindName("TitleInput")).Text = "Приготовить ужин";
        SaveImage(main, "editor.png");
        var save = Descendants<Button>(view).Single(x => ReferenceEquals(x.Command, editor.SaveCommand));
        save.Command!.Execute(null);
        editor.SaveCommand.Execute(null);
        await UntilAsync(() => model.Selected is not null && !model.IsBusy);
        Assert(service.ListAll().Count == 1, "repeated submission creates only one task");
        var id = model.Selected!.Item.Id;
        var creation = model.Selected.Item.CreatedAt;
        clock.Advance(TimeSpan.FromHours(2));
        main.EditCommand.Execute(null);
        await IdleAsync();
        view = (EditorView)((ContentControl)main.FindName("EditorHost")).Content;
        editor = (EditorViewModel)view.DataContext;
        ((TextBox)view.FindName("TitleInput")).Text = "Приготовить ужин на двоих";
        editor.SaveCommand.Execute(null);
        await UntilAsync(() => model.Selected?.Title.EndsWith("на двоих") == true && !model.IsBusy);
        Assert(model.Selected!.Item.Id == id && model.Selected.Item.CreatedAt == creation, "in-place editing preserves identity and original creation");
        var preview = Descendants<MarkdownView>(main).Single();
        Assert(new System.Windows.Documents.TextRange(preview.Document.ContentStart, preview.Document.ContentEnd).Text.Contains("паста"), "selected Markdown is rendered in details");

        service.Create("Отправить домашнюю работу", "");
        clock.Advance(TimeSpan.FromMinutes(1));
        var third = service.Create("Сдать отчёт по проекту", "## Остался последний шаг\n\nПроверить расчёты и оформить выводы.");
        service.Create("Пройти курс по C#", "");
        service.Create("Навести порядок на рабочем столе", "");
        service.Create("Помыть пол", "");
        service.Create("Очень длинное название задачи: подготовить материалы, проверить все расчёты и отправить подробный отчёт преподавателю", string.Join("\n\n", Enumerable.Repeat("Подробное описание с **важными деталями** и *пояснениями*.", 80)));
        await ExecuteAsync(model.AllCommand, model);
        SaveImage(main, "tasks.png");
        await ExecuteAsync(model.IncompleteCommand, model);
        var retained = model.Tasks.ToArray();
        var other = model.Tasks.Single(x => x.Item.Id == third.Id);
        var selectedBeforeToggle = model.Selected;
        model.ToggleCommand.Execute(other);
        await UntilAsync(() => !model.IsBusy);
        Assert(ReferenceEquals(model.Selected, selectedBeforeToggle), "row completion does not select a different card");
        Assert(model.Tasks.SequenceEqual(retained) && other.IsCompleted, "completion retains row objects and positions in Incomplete filter");
        Assert(other.Item.CompletedAt == clock.GetUtcNow(), "UI state uses the persisted application clock timestamp");
        await model.LoadAsync();
        Assert(model.Tasks.Count == 7 && other.IsCompleted, "ordinary refresh does not remove a completed row");
        SaveImage(main, "completed-retained.png");
        await ExecuteAsync(model.IncompleteCommand, model);
        Assert(model.Tasks.Count == 6 && model.Tasks.All(x => !x.IsCompleted), "explicit filter switch reapplies membership");
        await ExecuteAsync(model.AllCommand, model);
        Assert(model.Tasks.Last().IsCompleted, "All sorts incomplete before completed after explicit switch");
        await ExecuteAsync(model.HistoryCommand, model);
        Assert(model.Tasks.Single().Item.Id == third.Id, "history uses the same persisted task");
        var historyRow = model.Selected!;
        await ExecuteAsync(model.RestoreCommand, model);
        Assert(model.Tasks.Single() == historyRow && !historyRow.IsCompleted, "history restoration retains its row symmetrically");
        SaveImage(main, "history-restored.png");
        await ExecuteAsync(model.HistoryCommand, model);
        Assert(model.IsEmpty, "explicit section switch removes restored task from history");
        SaveImage(main, "history-empty.png");
        await ExecuteAsync(model.ActiveCommand, model);

        foreach (var width in new[] { 768, 375, 320 })
        {
            await SizeAsync(main, width, 1060);
            main.BackToListCommand.Execute(null);
            await IdleAsync();
            SaveImage(main, $"width-{width}.png");
            var nav = (ColumnDefinition)main.FindName("NavColumn");
            Assert(nav.ActualWidth == (width < 550 ? 58 : 178), "navigation width at " + width);
            Assert(((FrameworkElement)main.FindName("ListPane")).IsVisible && !((FrameworkElement)main.FindName("DetailPane")).IsVisible, "compact layout shows one pane");
            var list = (ListBox)main.FindName("TaskList");
            foreach (var scroll in Descendants<ScrollViewer>(main).Where(x => x.IsVisible))
                Assert(scroll.ExtentWidth <= scroll.ViewportWidth + 1, "no horizontal overflow at " + width);
            var longRow = model.Tasks.Last();
            list.SelectedItem = longRow;
            var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(longRow);
            list.ScrollIntoView(longRow);
            await IdleAsync();
            item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(longRow);
            item.Focus();
            item.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(main), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await IdleAsync();
            Assert(((FrameworkElement)main.FindName("DetailPane")).IsVisible, "keyboard opens compact details");
            SaveImage(main, $"detail-{width}.png");
            main.BackToListCommand.Execute(null);
            await IdleAsync();
            Assert(ReferenceEquals(Keyboard.FocusedElement, item), "back returns focus to the related card");
            main.EditCommand.Execute(null);
            await IdleAsync();
            SaveImage(main, $"editor-{width}.png");
            main.CancelEditorCommand.Execute(null);
        }
        await SizeAsync(main, 1010, 1060);
        model.Selected = model.Tasks.Last();
        SaveImage(main, "long-text.png");
        main.Close();

        var reopened = new MainViewModel(new TaskService(databasePath, clock));
        var reopenedWindow = new MainWindow(reopened);
        reopenedWindow.Show();
        await UntilAsync(() => !reopened.IsBusy);
        await reopened.LoadAsync();
        Assert(reopened.Tasks.Count == 7 && reopened.Tasks.Single(x => x.Item.Id == id).Description == markdown, "new WPF window reopens persisted tasks and raw Markdown");
        Assert(reopened.Tasks.Single(x => x.Item.Id == id).Item.CreatedAt == creation, "reopen preserves original creation");
        reopenedWindow.Width = 1600; reopenedWindow.Height = 1100;
        foreach (var scale in new[] { 1.5, 2.0 })
        {
            ((FrameworkElement)reopenedWindow.Content).LayoutTransform = new ScaleTransform(scale, scale);
            await IdleAsync();
            SaveImage(reopenedWindow, $"scale-{scale * 100:0}.png");
        }
        reopenedWindow.Close();
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        Assert(bindingErrors.Length == 0, "no XAML binding errors: " + bindingErrors);

        var blockedPath = Path.Combine(Path.GetDirectoryName(databasePath)!, "blocked");
        File.WriteAllText(blockedPath, "A file occupies the intended data directory");
        var unavailableService = new TaskService(Path.Combine(blockedPath, "tasks.db"));
        var unavailable = new MainViewModel(unavailableService);
        await unavailable.LoadAsync();
        Assert(unavailable.HasError && !unavailable.IsBusy, "read failure offers recoverable state");
        var unsaved = new EditorViewModel(unavailableService, null) { Title = "Сохранить позже", Description = "Не потерять **текст**" };
        unsaved.SaveCommand.Execute(null);
        await UntilAsync(() => !unsaved.IsBusy);
        Assert(unsaved.HasError && unsaved.Description.Contains("**текст**") && unsaved.Title == "Сохранить позже", "failed save retains input");
        File.Delete(blockedPath);
        await unavailable.LoadAsync();
        Assert(!unavailable.HasError && !unavailable.IsBusy, "retry recovers storage");

        var failurePath = Path.Combine(Path.GetDirectoryName(databasePath)!, "write-failure.db");
        var failureService = new TaskService(failurePath, clock);
        failureService.Initialize();
        failureService.Create("Не менять до записи", "Исходное описание");
        var failureModel = new MainViewModel(failureService);
        await failureModel.LoadAsync();
        var unchangedRow = failureModel.Selected!;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Move(failurePath, failurePath + ".backup");
        Directory.CreateDirectory(failurePath);
        await ExecuteAsync(failureModel.CompleteCommand, failureModel);
        Assert(failureModel.HasError && !unchangedRow.IsCompleted, "failed completion keeps previous visible state and reports no success");
        Directory.Delete(failurePath);
        File.Move(failurePath + ".backup", failurePath);
        await failureModel.LoadAsync();
        Assert(!failureModel.HasError && !failureModel.Selected!.IsCompleted, "failed write leaves persisted data unchanged after retry");

        var largeService = new TaskService(Path.Combine(Path.GetDirectoryName(databasePath)!, "large-list.db"), clock);
        largeService.Initialize();
        for (var i = 1; i <= 200; i++) largeService.Create($"Задача {i}: проверить длинный прокручиваемый список", "");
        var largeModel = new MainViewModel(largeService);
        var largeWindow = new MainWindow(largeModel);
        largeWindow.Show();
        await UntilAsync(() => !largeModel.IsBusy);
        await largeModel.LoadAsync();
        var largeList = (ListBox)largeWindow.FindName("TaskList");
        largeList.ScrollIntoView(largeModel.Tasks.Last());
        await IdleAsync();
        var listScroll = Descendants<ScrollViewer>(largeList).Single();
        Assert(largeModel.Tasks.Count == 200 && listScroll.ScrollableHeight > 0 && listScroll.VerticalOffset > 0, "200-task list scrolls to the end inside its pane");
        Assert(Descendants<Button>(largeWindow).Where(x => x.IsVisible).All(x => x.ActualHeight >= 44), "long-list actions retain their target height");
        SaveImage(largeWindow, "large-list.png");
        largeWindow.Close();
        await RunFieldsAsync(Path.Combine(Path.GetDirectoryName(databasePath)!, "field-ui.db"));
    }

    private static async Task ExecuteAsync(ICommand command, MainViewModel model)
    {
        Assert(command.CanExecute(null), "command is available");
        command.Execute(null);
        await UntilAsync(() => !model.IsBusy);
    }
    private static Task IdleAsync() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static async Task SizeAsync(Window window, double width, double height)
    {
        var root = (FrameworkElement)window.Content;
        window.Width = width + window.ActualWidth - root.ActualWidth;
        window.Height = height + window.ActualHeight - root.ActualHeight;
        await IdleAsync();
        window.UpdateLayout();
        Assert(Math.Abs(root.ActualWidth - width) < 1, "exact client width " + width);
    }
    private static async Task UntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow.AddSeconds(15);
        while (!predicate()) { if (DateTime.UtcNow > timeout) throw new TimeoutException("WPF scenario did not finish"); await Task.Delay(20); }
        await IdleAsync();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    internal static void SaveImage(Window window, string name)
    {
        void Trace(string step) { if (Environment.GetEnvironmentVariable("NOTIFS_QA_TRACE") == "1") Console.WriteLine($"SNAPSHOT {name}: {step}"); }
        Trace("layout");
        window.UpdateLayout();
        Trace("drawing");
        var root = (FrameworkElement)window.Content;
        var scale = root.LayoutTransform.Value;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * scale.M11), (int)Math.Ceiling(root.ActualHeight * scale.M22), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(root)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            // VisualBrush includes the root's LayoutTransform already; sampling unscaled bounds magnifies it twice.
            Viewbox = new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight),
            Stretch = Stretch.Fill
        }, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        Trace("render");
        bitmap.Render(visual);
        Trace("encode");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var output = Path.Combine(Directory.GetCurrentDirectory(), ".artifacts", "ui-qa");
        Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
        Trace("done");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private sealed class BindingErrorListener(System.Text.StringBuilder errors) : System.Diagnostics.TraceListener
    {
        public override void Write(string? message) => errors.Append(message);
        public override void WriteLine(string? message) => errors.AppendLine(message);
    }
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }
}
