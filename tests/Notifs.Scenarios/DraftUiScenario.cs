using System.Windows.Controls;
using System.Windows.Input;
using Notifs.App;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static partial class UiScenario
{
    private static async Task RunDraftsAsync(string databasePath)
    {
        var service = new TaskService(databasePath);
        service.Initialize();
        var a = service.Create("A", "сохранено");
        var model = new MainViewModel(service);
        var main = new MainWindow(model);
        main.Show();
        await UntilAsync(() => !model.IsBusy);
        main.NewCommand.Execute(null);
        await IdleAsync();
        var view = (EditorView)((ContentControl)main.FindName("EditorHost")).Content;
        ((TextBox)view.FindName("TitleInput")).Text = "Новый черновик";
        ((TextBox)view.FindName("DescriptionInput")).Text = "первая строка\nвторая строка";
        main.CancelEditorCommand.Execute(null);
        main.Close();
        main = new MainWindow(new MainViewModel(new TaskService(databasePath)));
        main.Show();
        await UntilAsync(() => !((MainViewModel)main.DataContext).IsBusy);
        main.NewCommand.Execute(null);
        await IdleAsync();
        view = (EditorView)((ContentControl)main.FindName("EditorHost")).Content;
        var editor = (EditorViewModel)view.DataContext;
        Assert(editor.Title == "Новый черновик" && editor.Description.Contains("вторая"), "WPF navigation and reopening restore the new draft");
        var input = (TextBox)view.FindName("DescriptionInput");
        input.Focus(); input.Select(0, 6);
        input.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice, new TextComposition(InputManager.Current, input, "*")) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
        Assert(input.Text.StartsWith("*первая* строка") && input.SelectedText == "первая", "literal star wraps and preserves selected text");
        input.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice, new TextComposition(InputManager.Current, input, "*")) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
        Assert(input.Text.StartsWith("**первая**"), "a second literal star adds a second pair");
        input.Undo();
        Assert(input.Text.StartsWith("*первая*"), "one undo reverts only the last wrapping action");
        input.Undo();
        Assert(input.Text == "первая строка\nвторая строка", "second undo restores original multiline text");
        input.Select(0, 6);
        Descendants<Button>(view).Single(x => Equals(x.Content, "Жирный")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        Descendants<Button>(view).Single(x => Equals(x.Content, "Курсив")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        Assert(input.Text.StartsWith("***первая***"), "italic toggle adds italic to bold selection rather than removing bold markers");
        Descendants<Button>(view).Single(x => Equals(x.Content, "Курсив")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        Descendants<Button>(view).Single(x => Equals(x.Content, "Жирный")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        Assert(input.Text.StartsWith("первая строка"), "repeating formatting buttons removes their respective formats");
        editor.DiscardCommand.Execute(null);
        Assert(editor.Title == "" && service.ReadDraft(null) is null && service.ListAll().Single() == a, "discard empties new form without creating a task");
        main.CancelEditorCommand.Execute(null);
        main.EditCommand.Execute(null);
        await IdleAsync();
        view = (EditorView)((ContentControl)main.FindName("EditorHost")).Content;
        editor = (EditorViewModel)view.DataContext;
        editor.PersonalEnabled = true;
        editor.PersonalDate = "21.11.2026";
        editor.PersonalTime = "15:30";
        editor.RepeatMinutes = "0";
        editor.SaveCommand.Execute(null);
        Assert(editor.HasError && service.ReadPersonal(a.Id) is null, "invalid personal schedule retains form without changing scheduler");
        editor.RepeatMinutes = "60";
        await SizeAsync(main, 1010, 1060);
        SaveImage(main, "issue-4-editor.png");
        foreach (var width in new[] { 768, 375, 320 })
        {
            await SizeAsync(main, width, 1060);
            ((System.Windows.FrameworkElement)view.FindName("PersonalExpander")).BringIntoView();
            await IdleAsync();
            foreach (var scroll in Descendants<ScrollViewer>(main).Where(x => x.IsVisible))
                Assert(scroll.ExtentWidth <= scroll.ViewportWidth + 1, "personal editor has no horizontal overflow at " + width);
            SaveImage(main, $"issue-4-editor-{width}.png");
        }
        await SizeAsync(main, 1010, 1060);
        editor.SaveCommand.Execute(null);
        await UntilAsync(() => service.ReadPersonal(a.Id) is not null && !((MainViewModel)main.DataContext).IsBusy);
        await IdleAsync();
        Assert(service.ReadPersonal(a.Id)?.Schedule.RepeatMinutes == 60 && service.ReadDraft(a.Id) is null, "saving editor atomically applies personal schedule and consumes only its draft");
        main.DeleteCommand.Execute(null);
        await IdleAsync();
        SaveImage(main, "issue-12-delete-confirmation.png");
        Assert(((System.Windows.FrameworkElement)main.FindName("DeleteOverlay")).IsVisible && service.ListAll().Count == 1, "delete opens an inline confirmation without changing data");
        Assert(Descendants<Button>(main).Single(x => Equals(x.Content, "Подтвердить")).IsEnabled, "confirmation action is immediately enabled for the captured task");
        main.CancelDeleteCommand.Execute(null);
        Assert(service.ListAll().Count == 1, "cancel leaves task and restore slot untouched");
        main.DeleteCommand.Execute(null);
        main.ConfirmDeleteCommand.Execute(null);
        await UntilAsync(() => service.ListAll().Count == 0 && !((MainViewModel)main.DataContext).IsBusy);
        Assert(((MainViewModel)main.DataContext).HasDeleted, "successful deletion exposes persistent restore action");
        ((MainViewModel)main.DataContext).RestoreDeletedCommand.Execute(null);
        await UntilAsync(() => service.ListAll().Count == 1 && !((MainViewModel)main.DataContext).IsBusy);
        Assert(service.ListAll().Single() == a, "WPF restore returns original task");
        main.Close();
    }
}
