using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Notifs.App;

public partial class MainWindow : Window
{
    public RelayCommand NewCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand BackToListCommand { get; }
    private readonly MainViewModel model;
    public MainWindow(MainViewModel model)
    {
        this.model = model;
        NewCommand = new(() => OpenEditor(false), () => !model.IsBusy && !model.HasError);
        EditCommand = new(() => OpenEditor(true), () => model.HasSelection && !model.IsBusy && !model.HasError);
        BackToListCommand = new(() => { model.Selected = null; TaskList.Focus(); });
        InitializeComponent();
        DataContext = model;
        InputBindings.Add(new KeyBinding(NewCommand, Key.N, ModifierKeys.Control));
        Loaded += async (_, _) => await model.LoadAsync();
        Shell.SizeChanged += (_, _) => AdaptLayout();
        model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(model.Selected)) AdaptLayout(); };
    }
    private async void OpenEditor(bool edit)
    {
        var editor = new EditorWindow(model.CreateEditor(edit)) { Owner = this };
        if (editor.ShowDialog() == true && editor.SavedId is { } id) await model.EditorSavedAsync(id, !edit);
    }
    private void AdaptLayout()
    {
        var compact = Shell.ActualWidth < 1040;
        NavColumn.Width = compact ? new GridLength(0) : new GridLength(220);
        NavRow.Height = compact ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(Navigation, 0); Grid.SetColumnSpan(Navigation, compact ? 2 : 1);
        Grid.SetRowSpan(Navigation, compact ? 1 : 2);
        Navigation.Padding = compact ? new Thickness(24, 8, 24, 8) : new Thickness(24, 32, 24, 32);
        Brand.Visibility = LocalNote.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        NavItems.Orientation = compact ? Orientation.Horizontal : Orientation.Vertical;
        Workspace.Margin = compact ? new Thickness(24, 20, 24, 16) : new Thickness(32, 32, 32, 16);
        var stacked = Shell.ActualWidth < 900;
        DetailColumn.Width = stacked ? new GridLength(0) : new GridLength(1.05, GridUnitType.Star);
        ListColumn.Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0.95, GridUnitType.Star);
        ListRow.Height = new GridLength(1, GridUnitType.Star);
        DetailRow.Height = new GridLength(0);
        Grid.SetColumn(DetailPane, stacked ? 0 : 1); Grid.SetRow(DetailPane, 0);
        ListPane.Margin = stacked ? new Thickness(0) : new Thickness(0, 0, 24, 0);
        BackToList.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
        ListPane.Visibility = stacked && model.HasSelection ? Visibility.Collapsed : Visibility.Visible;
        DetailPane.Visibility = stacked && !model.HasSelection ? Visibility.Collapsed : Visibility.Visible;
    }
}
