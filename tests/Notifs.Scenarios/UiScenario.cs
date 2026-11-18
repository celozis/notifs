using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Notifs.App;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static class UiScenario
{
    public static int Run(Application app, string databasePath)
    {
        var result = 1;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await RunAsync(databasePath);
                Console.WriteLine("PASS WPF create/edit/complete/history/restore/reopen, form validation and adaptive layouts");
                result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { app.Shutdown(); }
        });
        app.Run();
        return result;
    }

    private static async Task RunAsync(string databasePath)
    {
        var service = new TaskService(databasePath);
        var model = new MainViewModel(service);
        var main = new MainWindow(model);
        var bindingErrors = new System.Text.StringBuilder();
        var listener = new BindingErrorListener(bindingErrors);
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        main.Show();
        await UntilAsync(() => !model.IsBusy);
        await model.LoadAsync();
        var editor = model.CreateEditor(false);
        var dialog = new EditorWindow(editor) { Owner = main };
        await SubmitDialogAsync(dialog, async () =>
        {
            var description = (TextBox)dialog.FindName("DescriptionInput");
            description.Text = "# Небольшой план\n\n- **Купить** продукты\n- *Приготовить* ужин\n\n```text\nпаста + томаты + базилик\n```";
            editor.SaveCommand.Execute(null);
            Assert(editor.HasError && description.Text.Contains("паста"), "validation retains editor text");
            Assert(ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, dialog.FindName("TitleInput")), "validation focuses title");
            ((TextBox)dialog.FindName("TitleInput")).Text = "Приготовить ужин";
            var save = Descendants<Button>(dialog).Single(button => button.Content?.ToString() == "Сохранить задачу");
            Assert(save.Command is not null && save.Command.CanExecute(null), "save button binding");
            SaveImage(dialog, "editor-desktop.png");
            save.Command!.Execute(null);
            await UntilAsync(() => dialog.SavedId is not null);
        });
        await model.EditorSavedAsync(dialog.SavedId!.Value, true);
        Assert(model.Selected?.Title == "Приготовить ужин", "new task selected in real window");
        var creation = model.Selected!.Item.CreatedAt;
        var edit = model.CreateEditor(true);
        var editing = new EditorWindow(edit) { Owner = main };
        await SubmitDialogAsync(editing, async () =>
        {
            ((TextBox)editing.FindName("TitleInput")).Text = "Приготовить ужин на двоих";
            edit.SaveCommand.Execute(null);
            await UntilAsync(() => editing.SavedId is not null);
        });
        await model.EditorSavedAsync(editing.SavedId!.Value, false);
        await main.Dispatcher.InvokeAsync(System.Windows.Input.CommandManager.InvalidateRequerySuggested, DispatcherPriority.ApplicationIdle);
        Assert(model.Selected!.Item.CreatedAt == creation, "UI edit preserves creation time");

        var preview = Descendants<MarkdownView>(main).Single();
        Assert(new System.Windows.Documents.TextRange(preview.Document.ContentStart, preview.Document.ContentEnd).Text.Contains("паста"), "selected description binding renders Markdown");
        main.UpdateLayout();
        SaveImage(main, "tasks-desktop.png");

        model.CompleteCommand.Execute(null);
        await UntilAsync(() => !model.IsBusy);
        Assert(model.Tasks.Count == 0, "complete removes active task");
        model.HistoryCommand.Execute(null);
        await UntilAsync(() => !model.IsBusy);
        Assert(model.IsHistory && model.Selected!.Title.EndsWith("на двоих"), "history shows completed task");
        SaveImage(main, "history-desktop.png");
        model.RestoreCommand.Execute(null);
        await UntilAsync(() => !model.IsBusy);
        model.ActiveCommand.Execute(null);
        await UntilAsync(() => !model.IsBusy);
        Assert(model.Tasks.Count == 1 && model.Selected!.Item.CreatedAt == creation, "restored task in active list");

        main.Width = 760; main.Height = 560;
        await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        main.UpdateLayout();
        SaveImage(main, "tasks-compact.png");
        foreach (var button in Descendants<Button>(main).Where(button => button.IsVisible))
            Assert(button.ActualHeight >= 43 && button.ActualWidth >= 43, "compact action size");
        main.BackToListCommand.Execute(null);
        await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Assert(((FrameworkElement)main.FindName("ListPane")).IsVisible, "compact back action returns to task list");
        model.Selected = model.Tasks.Single();
        main.Close();

        var reopened = new MainViewModel(new TaskService(databasePath));
        var reopenedWindow = new MainWindow(reopened);
        reopenedWindow.Show();
        await UntilAsync(() => !reopened.IsBusy);
        await reopened.LoadAsync();
        Assert(reopened.Tasks.Count == 1 && reopened.Selected!.Description.Contains("паста"), "real window reopen retains task and Markdown");
        // Scale the workspace as a deterministic layout stress check; physical monitor DPI remains a separate check.
        reopenedWindow.Width = 1600; reopenedWindow.Height = 1100;
        ((FrameworkElement)reopenedWindow.Content).LayoutTransform = new ScaleTransform(1.5, 1.5);
        await reopenedWindow.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        SaveImage(reopenedWindow, "tasks-scale-150.png");
        ((FrameworkElement)reopenedWindow.Content).LayoutTransform = new ScaleTransform(2, 2);
        await reopenedWindow.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        SaveImage(reopenedWindow, "tasks-scale-200.png");
        reopenedWindow.Close();
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        Assert(bindingErrors.Length == 0, "XAML binding errors: " + bindingErrors);

        var blockedPath = Path.Combine(Path.GetDirectoryName(databasePath)!, "blocked");
        File.WriteAllText(blockedPath, "A file occupies the intended data directory");
        var unavailableService = new TaskService(Path.Combine(blockedPath, "tasks.db"));
        var unavailable = new MainViewModel(unavailableService);
        await unavailable.LoadAsync();
        Assert(unavailable.HasError && !unavailable.IsBusy, "storage startup failure is recoverable UI state");
        var unsaved = new EditorViewModel(unavailableService, null) { Title = "Сохранить позже", Description = "Не потерять **текст**" };
        unsaved.SaveCommand.Execute(null);
        await UntilAsync(() => !unsaved.IsBusy);
        Assert(unsaved.HasError && unsaved.Description.Contains("**текст**") && unsaved.Title == "Сохранить позже", "storage save failure retains form input");
        File.Delete(blockedPath);
        await unavailable.LoadAsync();
        Assert(!unavailable.HasError && !unavailable.IsBusy, "retry recovers once storage becomes available");
    }

    private static async Task SubmitDialogAsync(EditorWindow dialog, Func<Task> submit)
    {
        var task = dialog.Dispatcher.InvokeAsync(async () =>
        {
            await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await submit();
        }).Task.Unwrap();
        Assert(dialog.ShowDialog() == true, "editor saves through the real modal window");
        await task;
    }

    private static async Task UntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!predicate())
        {
            if (DateTime.UtcNow > timeout) throw new TimeoutException("WPF scenario did not finish");
            await Task.Delay(20);
        }
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
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
    private static void SaveImage(Window window, string name)
    {
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        var scale = root.LayoutTransform.Value;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * scale.M11), (int)Math.Ceiling(root.ActualHeight * scale.M22), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.Fill }, null,
                new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var output = Path.Combine(Directory.GetCurrentDirectory(), ".artifacts", "ui-qa");
        Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class BindingErrorListener(System.Text.StringBuilder errors) : System.Diagnostics.TraceListener
    {
        public override void Write(string? message) => errors.Append(message);
        public override void WriteLine(string? message) => errors.AppendLine(message);
    }
}
