using System.Windows;
using System.Windows.Input;

namespace Notifs.App;

public partial class MainWindow
{
    public RelayCommand FiltersCommand { get; private set; } = null!;
    private IInputElement? panelInitiator;
    private Action? panelReturnFocus;
    private void InitializeTaskPanels()
    {
        FiltersCommand = new(() =>
        {
            var view = new TaskFiltersView(model.Section, model.Settings, model.ApplySettingsAsync);
            view.CloseRequested += CloseTaskPanel;
            ShowTaskPanel(view);
        }, CanNavigate);
        Shell.SizeChanged += (_, _) => TaskPanelBorder.MaxHeight = Math.Min(850, Math.Max(100, Shell.ActualHeight - 32));
    }
    private void ShowTaskPanel(FrameworkElement view, Action? returnFocus = null)
    {
        TaskPanelBorder.MaxWidth = view is TaskFiltersView ? 670 : 620;
        panelInitiator = Keyboard.FocusedElement;
        panelReturnFocus = returnFocus;
        TaskPanelHost.Content = view; TaskPanelOverlay.Visibility = Visibility.Visible;
        Workspace.IsEnabled = Navigation.IsEnabled = false;
        TaskPanelBorder.MaxHeight = Math.Min(850, Math.Max(100, Shell.ActualHeight - 32));
        CommandManager.InvalidateRequerySuggested();
        if (view is not TaskFiltersView) Dispatcher.InvokeAsync(() => { if (TaskPanelOverlay.IsVisible) view.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); }, System.Windows.Threading.DispatcherPriority.Input);
    }
    private void CloseTaskPanel()
    {
        if (TaskPanelHost.Content is TaskFiltersView { IsBusy: true }) return;
        TaskPanelOverlay.Visibility = Visibility.Collapsed; TaskPanelHost.Content = null;
        Workspace.IsEnabled = Navigation.IsEnabled = true;
        CommandManager.InvalidateRequerySuggested();
        Dispatcher.InvokeAsync(() =>
        {
            if (panelReturnFocus is { } restore) restore();
            else if (panelInitiator is UIElement { IsVisible: true, IsEnabled: true } element) element.Focus(); else FocusSelected();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
