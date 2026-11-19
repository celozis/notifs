using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;

namespace Notifs.App;

public partial class MainWindow : Window
{
    public RelayCommand NewCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand BackToListCommand { get; }
    public RelayCommand CancelEditorCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public AsyncCommand RemindersCommand { get; }
    public RelayCommand RestoreDeletedCommand { get; }
    private RemindersViewModel? reminders;
    private Button? remindersNavigation;
    private readonly MainViewModel model;
    private EditorViewModel? editor;
    private readonly Dictionary<Guid, EditorViewModel> pendingEdits = [];
    private EditorViewModel? pendingNew;
    private bool showingDetails;
    private readonly List<(Button Button, TextBlock Label)> navigation = [];
    private Button? activeNavigation;
    private Button? historyNavigation;
    private Button? settingsNavigation;
    private readonly DispatcherTimer timeRefresh = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow(MainViewModel model)
    {
        this.model = model;
        InitializeDeletion();
        NewCommand = new(() => OpenEditor(false), () => CanNavigate() && !model.HasError);
        EditCommand = new(() => OpenEditor(true), () => model.HasSelection && CanNavigate() && !model.HasError);
        BackToListCommand = new(() => { showingDetails = false; AdaptLayout(); FocusSelected(); });
        CancelEditorCommand = new(CloseEditor, CanNavigate);
        SettingsCommand = new(() =>
        {
            if (editor?.PersistDraft() == false) return;
            CloseEditor();
            SettingsHost.Content = new SettingsView(new SettingsViewModel(model.Service));
            SettingsHost.Visibility = Visibility.Visible;
            BrowseView.Visibility = Visibility.Collapsed;
            AdaptLayout();
        }, CanNavigate);
        RemindersCommand = new(OpenRemindersAsync, CanNavigate);
        RestoreDeletedCommand = new(() => model.RestoreDeletedCommand.Execute(null),
            () => CanNavigate() && model.RestoreDeletedCommand.CanExecute(null));
        InitializeComponent();
        DataContext = model;
        AddNavigation("Новая задача", "M9,1 V17 M1,9 H17", NewCommand, primary: true);
        activeNavigation = AddNavigation("Мои задачи", "M1,3 L3,5 L6,1 M9,3 H17 M1,12 L3,14 L6,10 M9,12 H17", new AsyncCommand(() => NavigateAsync(false), CanNavigate));
        AddNavigation("В процессе", "M4,1 L16,9 L4,17 Z", null);
        AddNavigation("Календарь", "M2,3 H16 V17 H2 Z M5,0 V6 M13,0 V6 M2,8 H16 M6,11 H7 M11,11 H12", null);
        remindersNavigation = AddNavigation("Напоминания", "M3,13 H15 L13,10 V6 A4,4 0 0 0 5,6 V10 Z M7,16 Q9,19 11,16", RemindersCommand);
        historyNavigation = AddNavigation("История", "M2,2 V7 H7 M2,7 A7,7 0 1 1 2,13 M9,4 V10 L13,12", new AsyncCommand(() => NavigateAsync(true), CanNavigate));
        settingsNavigation = AddNavigation("Настройки", "M1,4 H17 M1,9 H17 M1,14 H17 M5,1 V7 M12,6 V12 M7,11 V17", SettingsCommand, bottom: true);
        InputBindings.Add(new KeyBinding(NewCommand, Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => editor?.SaveCommand.Execute(null), () => CanNavigate() && editor?.SaveCommand.CanExecute(null) == true), Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => { if (DeleteOverlay.IsVisible) CancelDeleteCommand.Execute(null); else if (RemindersHost.IsVisible && reminders?.CancelCommand.CanExecute(null) == true) reminders.CancelCommand.Execute(null); else if (CancelEditorCommand.CanExecute(null)) CancelEditorCommand.Execute(null); }), Key.Escape, ModifierKeys.None));
        model.DeletedRestored += async key =>
        {
            if (key.Type == "signal") await OpenRemindersAsync();
            else if (RemindersHost.IsVisible) CloseEditor();
        };
        Loaded += async (_, _) => await model.LoadAsync();
        timeRefresh.Tick += (_, _) => { if (!model.IsBusy) model.RefreshTime(); };
        Loaded += (_, _) => timeRefresh.Start();
        Closed += (_, _) => timeRefresh.Stop();
        Shell.SizeChanged += (_, _) => AdaptLayout();
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(model.Selected) or nameof(model.IsHistory)) AdaptLayout();
        };
        TaskList.PreviewMouseLeftButtonUp += (_, args) =>
        {
            if (FindAncestor<Button>(args.OriginalSource as DependencyObject) is not null) return;
            if (FindAncestor<ListBoxItem>(args.OriginalSource as DependencyObject) is not null) { showingDetails = true; AdaptLayout(); if (BackToList.IsVisible) BackToList.Focus(); }
        };
        TaskList.PreviewKeyDown += (_, args) =>
        {
            if (args.Key is Key.Enter or Key.Space && model.HasSelection && args.OriginalSource is ListBoxItem)
            { showingDetails = true; AdaptLayout(); BackToList.Focus(); args.Handled = true; }
        };
        Closing += (_, args) => { if (!CanNavigate() || editor?.PersistDraft() == false) args.Cancel = true; };
        AdaptLayout();
    }

    private bool CanNavigate() => !model.IsBusy && editor?.IsBusy != true && reminders?.IsBusy != true && DeleteOverlay?.IsVisible != true && (SettingsHost?.Content as SettingsView)?.DataContext is not SettingsViewModel { IsBusy: true };
    public async Task OpenRemindersAsync()
    {
        if (editor?.PersistDraft() == false) return;
        CloseEditor();
        reminders = new(model.Service);
        reminders.DeleteRequested += RequestSignalDeletion;
        reminders.ChangedStorage += async () => await model.LoadAsync();
        RemindersHost.Content = new RemindersView(reminders);
        RemindersHost.Visibility = Visibility.Visible;
        BrowseView.Visibility = Visibility.Collapsed;
        await reminders.LoadAsync();
        AdaptLayout();
    }
    public async Task OpenSignalAsync()
    {
        if (!CanNavigate()) throw new System.IO.IOException("Завершите текущее действие и повторите открытие.");
        await OpenRemindersAsync();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate();
    }
    public async Task OpenTaskAsync(Guid id)
    {
        if (!CanNavigate() || editor?.PersistDraft() == false || DeleteOverlay.IsVisible) throw new System.IO.IOException("Завершите текущее действие и повторите открытие.");
        CloseEditor();
        await model.SelectTaskAsync(id);
        showingDetails = true;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate(); AdaptLayout();
    }
    private async Task NavigateAsync(bool history)
    {
        if (editor?.PersistDraft() == false) return;
        CloseEditor();
        showingDetails = false;
        (history ? model.HistoryCommand : model.ActiveCommand).Execute(null);
        await Dispatcher.InvokeAsync(() => AdaptLayout(), DispatcherPriority.ApplicationIdle);
    }

    private Button AddNavigation(string name, string geometry, ICommand? command, bool primary = false, bool bottom = false)
    {
        var icon = new Path { Data = Geometry.Parse(geometry), Width = 18, Height = 18, Stretch = Stretch.Uniform,
            StrokeThickness = 1.7, Stroke = (Brush)FindResource(primary ? "OnPrimary" : "Ink"), VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = name, FontSize = 13, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon); content.Children.Add(label);
        var button = new Button { Content = content, Command = command, Style = (Style)FindResource(primary ? "PrimaryButton" : "NavigationButton"),
            Padding = new Thickness(10, 8, 6, 8), Margin = new Thickness(0, 0, 0, primary ? 20 : 8), ToolTip = command is null ? name + " — будет доступно в следующем этапе" : name,
            IsEnabled = command is not null };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetShowOnDisabled(button, true);
        (bottom ? BottomNavigation : NavItems).Children.Add(button);
        navigation.Add((button, label));
        return button;
    }

    private void OpenEditor(bool edit)
    {
        if (editor?.IsBusy == true) return;
        if (editor?.PersistDraft() == false) return;
        SettingsHost.Visibility = Visibility.Collapsed;
        RemindersHost.Visibility = Visibility.Collapsed;
        var originalId = edit ? model.Selected?.Item.Id : null;
        editor = originalId is { } id && pendingEdits.TryGetValue(id, out var pending) ? pending : !edit && pendingNew is not null ? pendingNew : model.CreateEditor(edit);
        if (originalId is { } key) pendingEdits[key] = editor; else pendingNew = editor;
        var currentEditor = editor;
        // Subscribe once per presentation, then remove the callback after saving or leaving.
        if (EditorHost.Content is EditorView previous) DetachEditor(previous);
        var view = new EditorView(editor);
        Action<Guid> onSaved = async savedId =>
        {
            currentEditor.Saved -= EditorSaved;
            if (originalId is { } editId) pendingEdits.Remove(editId); else pendingNew = null;
            CloseEditor();
            await model.EditorSavedAsync(savedId, !edit);
            showingDetails = true;
            AdaptLayout();
            FocusSelected();
        };
        savedCallback = onSaved;
        currentEditor.Saved += EditorSaved;
        EditorHost.Content = view;
        EditorHost.Visibility = Visibility.Visible;
        BrowseView.Visibility = Visibility.Collapsed;
        StatusNote.SetCurrentValue(TextBlock.TextProperty, "Изменения сохранятся по кнопке «Сохранить задачу»");
    }
    private Action<Guid>? savedCallback;
    private void EditorSaved(Guid id) => savedCallback?.Invoke(id);
    private void DetachEditor(EditorView view) { if (view.DataContext is EditorViewModel previous) previous.Saved -= EditorSaved; }
    private void CloseEditor()
    {
        if (editor?.PersistDraft() == false) return;
        if (EditorHost.Content is EditorView view) DetachEditor(view);
        editor = null;
        showingDetails = false;
        savedCallback = null;
        EditorHost.Content = null;
        EditorHost.Visibility = Visibility.Collapsed;
        SettingsHost.Visibility = Visibility.Collapsed;
        RemindersHost.Visibility = Visibility.Collapsed;
        BrowseView.Visibility = Visibility.Visible;
        StatusNote.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(model.Status)));
        AdaptLayout();
        FocusSelected();
    }
    private void FocusSelected() => Dispatcher.InvokeAsync(() =>
    {
        if (model.Selected is not null) TaskList.ScrollIntoView(model.Selected);
        if (model.Selected is not null && TaskList.ItemContainerGenerator.ContainerFromItem(model.Selected) is ListBoxItem item) item.Focus();
        else if (TaskList.IsVisible) TaskList.Focus();
        if (!TaskList.IsVisible && BackToList.IsVisible) BackToList.Focus();
    }, DispatcherPriority.Input);

    // A nested button must not trigger ListBoxItem selection on the bubbling mouse event.
    private void CompletionMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton == MouseButton.Left && sender is Button { Command: { } command } button)
        {
            if (command.CanExecute(button.CommandParameter)) command.Execute(button.CommandParameter);
            args.Handled = true;
        }
    }
    private static T? FindAncestor<T>(DependencyObject? item) where T : DependencyObject
    {
        while (item is not null) { if (item is T match) return match; item = item is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(item); }
        return null;
    }
    private void AdaptLayout()
    {
        var width = Shell.ActualWidth > 0 ? Shell.ActualWidth : Width;
        var iconsOnly = width < 550;
        NavColumn.Width = new GridLength(iconsOnly ? 58 : 178);
        Navigation.Padding = iconsOnly ? new Thickness(7, 30, 6, 16) : new Thickness(12, 30, 12, 16);
        Brand.Margin = iconsOnly ? new Thickness(0, 0, 0, 28) : new Thickness(8, 0, 0, 28);
        BrandName.Visibility = BrandTagline.Visibility = LocalNote.Visibility = iconsOnly ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (button, label) in navigation)
        {
            label.Visibility = iconsOnly ? Visibility.Collapsed : Visibility.Visible;
            button.Padding = iconsOnly ? new Thickness(0) : new Thickness(10, 8, 6, 8);
            button.HorizontalContentAlignment = iconsOnly ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            button.MinWidth = 44;
        }
        var stacked = width < 850;
        ListColumn.Width = new GridLength(1, GridUnitType.Star);
        GapColumn.Width = new GridLength(stacked ? 0 : 20);
        DetailColumn.Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(DetailPane, stacked ? 0 : 2);
        BackToList.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
        ListPane.Visibility = stacked && showingDetails && model.HasSelection ? Visibility.Collapsed : Visibility.Visible;
        DetailPane.Visibility = stacked && (!showingDetails || !model.HasSelection) ? Visibility.Collapsed : Visibility.Visible;
        FilterBar.Visibility = model.IsHistory ? Visibility.Collapsed : Visibility.Visible;
        var settingsOpen = SettingsHost.IsVisible;
        var remindersOpen = RemindersHost.IsVisible;
        if (activeNavigation is not null) activeNavigation.Background = (Brush)FindResource(model.IsHistory || settingsOpen || remindersOpen ? "Navigation" : "Selected");
        if (historyNavigation is not null) historyNavigation.Background = (Brush)FindResource(model.IsHistory && !settingsOpen && !remindersOpen ? "Selected" : "Navigation");
        if (settingsNavigation is not null) settingsNavigation.Background = (Brush)FindResource(settingsOpen ? "Selected" : "Navigation");
        if (remindersNavigation is not null) remindersNavigation.Background = (Brush)FindResource(remindersOpen ? "Selected" : "Navigation");
    }
}
