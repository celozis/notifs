using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;

namespace Notifs.App;

public partial class EditorView : UserControl
{
    public EditorView(EditorViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        DescriptionInput.PreviewTextInput += (_, args) =>
        {
            if (args.Text == "*" && DescriptionInput.SelectionLength > 0) { FormatSelection(1, false); args.Handled = true; }
        };
        DescriptionInput.InputBindings.Add(new KeyBinding(new RelayCommand(() => FormatSelection(2, true)), Key.B, ModifierKeys.Control));
        DescriptionInput.InputBindings.Add(new KeyBinding(new RelayCommand(() => FormatSelection(1, true)), Key.I, ModifierKeys.Control));
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
    private void BoldClick(object sender, RoutedEventArgs args) => FormatSelection(2, true);
    private void ItalicClick(object sender, RoutedEventArgs args) => FormatSelection(1, true);
    private void FormatSelection(int count, bool toggle)
    {
        var input = DescriptionInput;
        if (input.SelectionLength == 0) { input.Focus(); return; }
        var start = input.SelectionStart;
        var length = input.SelectionLength;
        var stars = new string('*', count);
        var before = 0; var after = 0;
        while (start - before > 0 && input.Text[start - before - 1] == '*') before++;
        while (start + length + after < input.Text.Length && input.Text[start + length + after] == '*') after++;
        var surrounding = Math.Min(before, after);
        var remove = toggle && (count == 1 ? surrounding % 2 == 1 : surrounding >= 2);
        input.BeginChange();
        try
        {
            var content = input.SelectedText;
            if (remove) { input.Select(start - count, length + count * 2); input.SelectedText = content; input.Select(start - count, length); }
            else { input.SelectedText = stars + content + stars; input.Select(start + count, length); }
        }
        finally { input.EndChange(); }
        input.Focus();
    }
    private void FocusInvalid()
    {
        if (DataContext is EditorViewModel model && FindName(model.InvalidField) is Control input)
        { if (model.InvalidField.StartsWith("Personal") || model.InvalidField == "RepeatMinutesInput") PersonalExpander.IsExpanded = true; input.BringIntoView(); input.Focus(); }
    }
}
