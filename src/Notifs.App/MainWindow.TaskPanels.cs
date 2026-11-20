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
        Shell.SizeChanged += (_, _) => TaskPanelBorder.MaxHeight = Math.Max(100, Shell.ActualHeight - 32);
    }
    private void ShowTaskPanel(FrameworkElement view, Action? returnFocus = null)
    {
        panelInitiator = Keyboard.FocusedElement;
        panelReturnFocus = returnFocus;
        TaskPanelHost.Content = view; TaskPanelOverlay.Visibility = Visibility.Visible;
        Workspace.IsEnabled = Navigation.IsEnabled = false;
        TaskPanelBorder.MaxHeight = Math.Max(100, Shell.ActualHeight - 32);
        CommandManager.InvalidateRequerySuggested();
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
