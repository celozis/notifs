using System.Windows;
using System.Windows.Input;
using System.Windows.Automation;

namespace Notifs.App;

public partial class MainWindow
{
    public RelayCommand DeleteCommand { get; private set; } = null!;
    public RelayCommand CancelDeleteCommand { get; private set; } = null!;
    public AsyncCommand ConfirmDeleteCommand { get; private set; } = null!;
    private Guid? deletingId;
    private bool deletingSignal;
    private IInputElement? deleteInitiator;

    private void InitializeDeletion()
    {
        DeleteCommand = new(() =>
        {
            var task = editor is null ? model.Selected?.Item : editor.SavedTask;
            if (task is null || editor?.PersistDraft() == false) return;
            deletingSignal = false;
            ShowDeletion(task.Id, "Удалить задачу?", $"«{task.Title}» исчезнет из списка и календаря. Последнее удаление можно восстановить.");
        }, () => (editor is null ? model.HasSelection : editor.CanDelete) && CanNavigate());
        CancelDeleteCommand = new(CloseDelete, () => !model.IsBusy);
        ConfirmDeleteCommand = new(async () =>
        {
            if (deletingId is not { } id) return;
            if (deletingSignal)
            {
                signalDeleteBusy = true;
                try { await Task.Run(() => model.Service.DeleteSignal(id)); }
                catch (Exception ex) when (MainViewModel.IsStorageError(ex)) { DeleteError.Text = "Не удалось удалить напоминание. Попробуйте ещё раз."; return; }
                finally { signalDeleteBusy = false; }
                CloseDelete();
                if (reminders is not null) await reminders.LoadAsync();
                await model.LoadAsync();
                return;
            }
            if (!await model.DeleteAsync(id)) { DeleteError.Text = model.Error; return; }
            pendingEdits.Remove(id);
            // The stored draft was included in the deletion transaction; do not write it back.
            if (EditorHost.Content is EditorView view) DetachEditor(view);
            editor = null;
            CloseEditor();
            CloseDelete();
            FocusSelected();
        }, () => deletingId is not null && !model.IsBusy && !signalDeleteBusy);
    }

    private bool signalDeleteBusy;
    private void RequestSignalDeletion(Notifs.Core.SignalReminder item)
    {
        deletingSignal = true;
        ShowDeletion(item.Id, "Удалить напоминание?", $"«{item.Title}» перестанет напоминать. Расписание, текущее дело и прошлые выполнения исчезнут из календаря. Последнее удаление можно восстановить; расписание вернётся выключенным.");
    }
    private void ShowDeletion(Guid id, string heading, string message)
    {
        deletingId = id;
        deleteInitiator = Keyboard.FocusedElement;
        DeleteHeading.Text = heading;
        AutomationProperties.SetName(DeleteOverlay, heading);
        DeleteMessage.Text = message;
        DeleteError.Text = "";
        DeleteOverlay.Visibility = Visibility.Visible;
        Workspace.IsHitTestVisible = Navigation.IsHitTestVisible = false;
        CommandManager.InvalidateRequerySuggested(); LeaveTask.Focus();
    }

    private void CloseDelete()
    {
        if (model.IsBusy || signalDeleteBusy) return;
        DeleteOverlay.Visibility = Visibility.Collapsed;
        Workspace.IsHitTestVisible = Navigation.IsHitTestVisible = true;
        deletingId = null;
        CommandManager.InvalidateRequerySuggested();
        if (deleteInitiator is UIElement { IsVisible: true, IsEnabled: true } element) element.Focus();
        else FocusSelected();
    }
}
