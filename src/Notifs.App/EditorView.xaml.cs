using System.Windows.Controls;

namespace Notifs.App;

public partial class EditorView : UserControl
{
    public EditorView(EditorViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Loaded += (_, _) => { model.ValidationFailed += FocusInvalid; TitleInput.Focus(); };
        Unloaded += (_, _) => model.ValidationFailed -= FocusInvalid;
        SizeChanged += (_, _) =>
        {
            var columns = ActualWidth < 450 ? 1 : 2;
            FieldsPanel.ColumnDefinitions[1].Width = columns == 1 ? new System.Windows.GridLength(0) : new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
            for (var i = 0; i < FieldsPanel.Children.Count; i++)
            { Grid.SetRow(FieldsPanel.Children[i], i / columns); Grid.SetColumn(FieldsPanel.Children[i], i % columns); }
        };
    }
    private void FocusInvalid()
    {
        if (DataContext is EditorViewModel model && FindName(model.InvalidField) is Control input)
        { input.BringIntoView(); input.Focus(); }
    }
}
