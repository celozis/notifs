using System.Windows.Controls;

namespace Notifs.App;

public partial class EditorView : UserControl
{
    public EditorView(EditorViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Loaded += (_, _) => { model.ValidationFailed += FocusTitle; TitleInput.Focus(); };
        Unloaded += (_, _) => model.ValidationFailed -= FocusTitle;
    }
    private void FocusTitle() => TitleInput.Focus();
}
