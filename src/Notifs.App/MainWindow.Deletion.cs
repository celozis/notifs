using System.Windows;
using System.Windows.Input;

namespace Notifs.App;

public partial class MainWindow
{
    public RelayCommand DeleteCommand { get; private set; } = null!;
    public RelayCommand CancelDeleteCommand { get; private set; } = null!;
    public AsyncCommand ConfirmDeleteCommand { get; private set; } = null!;
    private Guid? deletingId;
    private IInputElement? deleteInitiator;

    private void InitializeDeletion()
    {
        DeleteCommand = new(() =>
        {
            if (model.Selected is not { } row || editor?.PersistDraft() == false) return;
            deletingId = row.Item.Id;
            deleteInitiator = Keyboard.FocusedElement;
            DeleteMessage.Text = $"«{row.Title}» исчезнет из списка и календаря. Последнее удаление можно восстановить.";
            DeleteError.Text = "";
            DeleteOverlay.Visibility = Visibility.Visible;
            Workspace.IsHitTestVisible = Navigation.IsHitTestVisible = false;
            CommandManager.InvalidateRequerySuggested();
            LeaveTask.Focus();
        }, () => model.HasSelection && CanNavigate());
        CancelDeleteCommand = new(CloseDelete, () => !model.IsBusy);
        ConfirmDeleteCommand = new(async () =>
        {
            if (deletingId is not { } id) return;
            if (!await model.DeleteAsync(id)) { DeleteError.Text = model.Error; return; }
            pendingEdits.Remove(id);
            // The stored draft was included in the deletion transaction; do not write it back.
            if (EditorHost.Content is EditorView view) DetachEditor(view);
            editor = null;
            CloseEditor();
            CloseDelete();
            FocusSelected();
        }, () => deletingId is not null && !model.IsBusy);
    }

    private void CloseDelete()
    {
        if (model.IsBusy) return;
        DeleteOverlay.Visibility = Visibility.Collapsed;
        Workspace.IsHitTestVisible = Navigation.IsHitTestVisible = true;
        deletingId = null;
        CommandManager.InvalidateRequerySuggested();
        if (deleteInitiator is UIElement { IsVisible: true, IsEnabled: true } element) element.Focus();
        else FocusSelected();
    }
}
