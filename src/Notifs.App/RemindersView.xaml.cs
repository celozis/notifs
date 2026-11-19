using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Notifs.App;

public partial class RemindersView : UserControl
{
    public static readonly DependencyProperty IsNarrowProperty = DependencyProperty.Register(nameof(IsNarrow), typeof(bool), typeof(RemindersView));
    public bool IsNarrow { get => (bool)GetValue(IsNarrowProperty); set => SetValue(IsNarrowProperty, value); }
    public RemindersView(RemindersViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        model.FocusEditor += FocusEditor;
        model.FocusList += FocusList;
        model.ToggleSaved += FocusToggle;
        model.ValidationFailed += FocusInvalid;
        Unloaded += (_, _) => { model.FocusEditor -= FocusEditor; model.FocusList -= FocusList; model.ToggleSaved -= FocusToggle; model.ValidationFailed -= FocusInvalid; };
        InputBindings.Add(new KeyBinding(model.SaveCommand, Key.S, ModifierKeys.Control));
        SizeChanged += (_, _) =>
        {
            IsNarrow = ActualWidth < 450;
            Form.Width = Math.Min(700, Math.Max(0, ActualWidth - SystemParameters.VerticalScrollBarWidth));
            foreach (var grid in new[] { ModeFields, StartFields, ListHeading })
            {
                var columns = ActualWidth < 450 ? 1 : 2;
                grid.ColumnDefinitions[1].Width = columns == 1 ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
                for (var i = 0; i < grid.Children.Count; i++) { Grid.SetColumn(grid.Children[i], i % columns); Grid.SetRow(grid.Children[i], i / columns); }
            }
        };
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.IsEditing) && !model.IsEditing)
                Dispatcher.InvokeAsync(() => NewReminder.Focus(), DispatcherPriority.Input);
        };
    }
    private void FocusEditor() => Dispatcher.InvokeAsync(() => { EditorScroll.ScrollToTop(); TitleInput.Focus(); }, DispatcherPriority.Input);
    private void FocusList() => Dispatcher.InvokeAsync(() => NewReminder.Focus(), DispatcherPriority.Input);
    private void FocusToggle(Guid id) => Dispatcher.InvokeAsync(() => FindToggle(this, id)?.Focus(), DispatcherPriority.Input);
    private static Button? FindToggle(DependencyObject parent, Guid id)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Button { DataContext: SignalRow row } button && row.Item.Id == id && button.CommandParameter == row &&
                button.Command is ParameterAsyncCommand && Equals(button.Content, row.ToggleLabel)) return button;
            if (FindToggle(child, id) is { } found) return found;
        }
        return null;
    }
    private void FocusInvalid()
    {
        if (DataContext is not RemindersViewModel model) return;
        var target = FindName(model.InvalidField) as FrameworkElement;
        if (target == DaysInput) target = FindCheckBox(DaysInput);
        target?.BringIntoView(); target?.Focus();
    }
    private static CheckBox? FindCheckBox(DependencyObject parent)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is CheckBox box) return box;
            if (FindCheckBox(child) is { } found) return found;
        }
        return null;
    }
}
