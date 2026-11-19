using System.Windows.Controls;

namespace Notifs.App;

public partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel model) { InitializeComponent(); DataContext = model; Loaded += (_, _) => SnoozeInput.Focus(); }
}
