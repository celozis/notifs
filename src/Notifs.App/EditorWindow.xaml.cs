using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace Notifs.App;

public partial class EditorWindow : Window
{
    public RelayCommand CancelCommand { get; }
    public Guid? SavedId { get; private set; }
    private readonly EditorViewModel model;
    public EditorWindow(EditorViewModel model)
    {
        this.model = model;
        CancelCommand = new(Close, () => !model.IsBusy);
        InitializeComponent();
        DataContext = model;
        InputBindings.Add(new KeyBinding(model.SaveCommand, Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(CancelCommand, Key.Escape, ModifierKeys.None));
        Loaded += (_, _) => TitleInput.Focus();
        model.ValidationFailed += () => TitleInput.Focus();
        model.Saved += id => { SavedId = id; DialogResult = true; };
        Closing += ConfirmDiscard;
    }
    private void ConfirmDiscard(object? sender, CancelEventArgs e)
    {
        if (SavedId is not null) return;
        if (model.IsBusy) { e.Cancel = true; return; }
        if (model.IsDirty && MessageBox.Show(this, "В задаче есть несохранённые изменения. Закрыть форму и отказаться от них?", "Несохранённая задача", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            e.Cancel = true;
    }
}
