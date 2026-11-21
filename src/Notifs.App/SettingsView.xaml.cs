using System.Windows;
using System.Windows.Controls;

namespace Notifs.App;

public partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel model)
    {
        InitializeComponent(); DataContext = model;
        Loaded += (_, _) => { Adapt(); SnoozeInput.Focus(); };
        SizeChanged += (_, _) => Adapt();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Unloaded += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        model.InvalidField += name => Dispatcher.InvokeAsync(() =>
        {
            if (FindName(name) is FrameworkElement input) { input.BringIntoView(); input.Focus(); }
        });
    }
    private void DisplayChanged(object? sender, EventArgs args) => Dispatcher.BeginInvoke(() => ((SettingsViewModel)DataContext).RefreshMonitors());
    private void Adapt()
    {
        var stacked = ActualWidth < 680;
        SecondColumn.Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        SectionGap.Width = new GridLength(stacked ? 0 : 18);
        Grid.SetRow(SummarySection, stacked ? 1 : 0);
        Grid.SetColumn(SummarySection, stacked ? 0 : 2);
        SummarySection.Margin = stacked ? new Thickness(0, 18, 0, 0) : new Thickness(0);
        Grid.SetRow(BackgroundSection, stacked ? 2 : 1);
    }
}
