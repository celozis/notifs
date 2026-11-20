using System.ComponentModel;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Notifs.App;

/// <summary>Owns the tray and the explicit exit of the process-wide scheduler.</summary>
public sealed class DesktopLifetime : IDisposable
{
    private readonly MainWindow main;
    private readonly NotificationRuntime notifications;
    private readonly Forms.NotifyIcon tray;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly System.Drawing.Icon icon;
    private readonly Action shutdown;
    private bool exiting, closed, disposed;

    public DesktopLifetime(MainWindow main, NotificationRuntime notifications, Action shutdown)
    {
        this.main = main; this.notifications = notifications; this.shutdown = shutdown;
        icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "notifs.ico"));
        menu.Items.Add("Открыть Notifs", null, (_, _) => Show());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Exit());
        tray = new Forms.NotifyIcon { Text = "Notifs — задачи и напоминания", Icon = icon, ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) Show(); };
        main.Closing += Closing;
        main.Closed += Closed;
    }
    public void Show()
    {
        if (closed) return;
        main.Show();
        if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
        main.Activate();
    }
    private void Closing(object? sender, CancelEventArgs args)
    {
        if (exiting) return;
        args.Cancel = true;
        main.Hide();
    }
    public bool Exit()
    {
        if (closed) return true;
        exiting = true;
        main.Close();
        if (!closed) { exiting = false; Show(); return false; }
        return true;
    }
    private void Closed(object? sender, EventArgs args)
    {
        closed = true;
        notifications.Dispose();
        Dispose();
        shutdown();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        main.Closing -= Closing; main.Closed -= Closed;
        tray.Visible = false; tray.Dispose(); menu.Dispose(); icon.Dispose();
    }
}
