using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Notifs.Core;

namespace Notifs.App;

/// <summary>Native primary-monitor presentation. Domain actions are supplied by the application.</summary>
public sealed class NativeNotificationOutput : INotificationOutput, IDisposable
{
    private readonly StackPanel panel = new() { Background = Brushes.Transparent };
    private readonly MediaPlayer player = new();
    private bool disposed;
    private NotificationMonitor? monitor = WindowsNotificationMonitors.List().FirstOrDefault(x => x.IsPrimary);
    public Window Window { get; }
    public IReadOnlyList<NotificationEvent> VisibleEvents { get; private set; } = [];
    public Action<Guid>? HideRequested { get; set; }
    public Action? HideAllRequested { get; set; }
    public Func<NotificationEvent, NotificationSubject, string, Task>? ActionRequested { get; set; }
    public Func<int> SnoozeMinutes { get; set; } = () => 30;
    public Action<bool>? HoverChanged { get; set; }
    public void SetMonitor(NotificationMonitor? value)
    {
        if (monitor == value) return;
        monitor = value;
        if (value == null) { Window.Hide(); return; }
        var hwnd = new WindowInteropHelper(Window).EnsureHandle();
        SetWindowPos(hwnd, -1, value.WorkArea.Left + 1, value.WorkArea.Top + 1, 0, 0, 0x0011);
        if (VisibleEvents.Count > 0) Render(VisibleEvents);
    }

    public NativeNotificationOutput()
    {
        Window = new Window
        {
            Title = "Notifs — уведомления", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, SizeToContent = SizeToContent.Height,
            Width = 365, MinWidth = 0, MinHeight = 0, Content = panel,
            Left = 0, Top = 0
        };
        Window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(Window).Handle;
            SetWindowLongPtr(hwnd, -20, GetWindowLongPtr(hwnd, -20) | 0x08000000 | 0x00000080); // NOACTIVATE, TOOLWINDOW
            HwndSource.FromHwnd(hwnd)?.AddHook((nint handle, int message, nint wParam, nint lParam, ref bool handled) =>
            {
                if (message == 0x0021) { handled = true; return 3; } // MA_NOACTIVATE keeps explicit clicks usable.
                return 0;
            });
        };
        Window.SizeChanged += (_, _) => Position();
        Window.MouseEnter += (_, _) => HoverChanged?.Invoke(true);
        Window.MouseLeave += (_, _) => HoverChanged?.Invoke(false);
        SystemParameters.StaticPropertyChanged += ParametersChanged;
    }

    private void ParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (disposed || VisibleEvents.Count == 0) return;
        Window.Dispatcher.BeginInvoke(() => Render(VisibleEvents));
    }

    public void Render(IReadOnlyList<NotificationEvent> events)
    {
        if (disposed) return;
        var previous = VisibleEvents.Select(x => x.Id).ToHashSet();
        VisibleEvents = events.ToArray();
        panel.Children.Clear();
        if (events.Count == 0) { Window.Hide(); return; }
        if (monitor == null) return;
        // Creating the HWND at the primary origin gives WPF the correct DPI before measuring.
        new WindowInteropHelper(Window).EnsureHandle();
        var work = monitor.WorkArea;
        var dpi = VisualTreeHelper.GetDpi(Window).DpiScaleX;
        Window.Width = Math.Max(1, Math.Min(365, (work.Right - work.Left) / dpi - 44));
        var height = Math.Max(1, (work.Bottom - work.Top) / dpi - 44);
        var hideAll = new Button { Content = "Скрыть все", HorizontalAlignment = HorizontalAlignment.Right, FontSize = 11, MinHeight = 44, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 0, 10) };
        hideAll.Click += (_, _) => HideAllRequested?.Invoke();
        panel.Children.Add(hideAll);
        var maximumCard = Math.Max(1, (height - 54 - (events.Count - 1) * 10) / events.Count);
        foreach (var item in events)
        {
            var card = CreateCard(item, maximumCard);
            card.Margin = new Thickness(0, panel.Children.Count > 1 ? 10 : 0, 0, 0);
            panel.Children.Add(card);
            if (!previous.Contains(item.Id) && SystemParameters.ClientAreaAnimation)
            {
                var transform = new TranslateTransform();
                card.RenderTransform = transform;
                transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            }
        }
        Window.MaxHeight = height;
        Window.UpdateLayout();
        if (!Window.IsVisible) Window.Show();
        Position();
    }

    private Border CreateCard(NotificationEvent item, double maximumHeight)
    {
        var normal = ((SolidColorBrush)Resource("NotificationSurface")).Color;
        var background = new SolidColorBrush(normal);
        var card = new Border { Background = background, BorderBrush = Resource("Line"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), MaxHeight = maximumHeight };
        var content = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var close = new Button { Content = "×", Width = 44, Height = 44, MinWidth = 44, Padding = new Thickness(0), FontSize = 20, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        AutomationProperties.SetName(close, "Скрыть уведомление");
        close.Click += (_, _) => HideRequested?.Invoke(item.Id);
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = "NOTIFS · " + KindLabel(item.Kind), FontSize = 10, Foreground = Resource("Primary"), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(header);
        var error = new TextBlock { Foreground = Resource("Danger"), FontSize = 12, Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        if (item.Kind is NotificationKind.Summary or NotificationKind.Missed)
        {
            content.Children.Add(new TextBlock { Text = item.Kind == NotificationKind.Missed ? "Пропущенные напоминания" : "Вернёмся к делам?", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
            foreach (var subject in item.Subjects)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                if (subject.Rank is { } rank) row.Children.Add(new RankBadge { Rank = rank, Style = (Style)Window.FindResource("RankBadge"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0) });
                var text = new StackPanel(); Grid.SetColumn(text, 1);
                text.Children.Add(new TextBlock { Text = subject.Title, FontSize = 15, FontWeight = FontWeights.SemiBold });
                text.Children.Add(new TextBlock { Text = subject.Detail, FontSize = 12, Foreground = Resource("MutedInk") });
                row.Children.Add(text); content.Children.Add(row);
            }
        }
        else
        {
        if (item.Kind == NotificationKind.CheckIn) content.Children.Add(new TextBlock { Text = "Сделал задачу?", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        foreach (var subject in item.Subjects)
        {
            var open = new Button { Content = new TextBlock { Text = subject.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), MinHeight = 44, Margin = new Thickness(0, 0, 0, 8) };
            AutomationProperties.SetName(open, "Открыть: " + subject.Title);
            open.Click += async (_, _) => await InvokeAsync(item, subject, "open", open, error);
            content.Children.Add(open);
            content.Children.Add(new TextBlock { Text = subject.Detail, FontSize = 12, Foreground = Resource("MutedInk"), Margin = new Thickness(0, 0, 0, 14) });
            var actions = new WrapPanel();
            foreach (var action in item.Actions ?? [])
            {
                var button = new Button { Content = action.Id == "snooze" ? $"Отложить на {SnoozeMinutes()} мин" : action.Label,
                    FontSize = 11, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 6, 6) };
                if (action.Id == "complete") button.Style = (Style)Window.FindResource("PrimaryButton");
                button.Click += async (_, _) => await InvokeAsync(item, subject, action.Id, button, error);
                actions.Children.Add(button);
            }
            content.Children.Add(actions);
        }
        }
        content.Children.Add(error);
        var layout = new Grid();
        if (item.Kind == NotificationKind.Personal) layout.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Background = Resource("Primary"), CornerRadius = new CornerRadius(12, 0, 0, 12) });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(new ScrollViewer { Content = content, Margin = new Thickness(18, 15, 17, 16), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        if (item.Kind is NotificationKind.Summary or NotificationKind.Missed)
        {
            var openTasks = new Button { Content = "Открыть задачи", Style = (Style)Window.FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(18, 0, 17, 16) };
            openTasks.Click += async (_, _) => await InvokeAsync(item, item.Subjects[0], "open-tasks", openTasks, error);
            Grid.SetRow(openTasks, 1); layout.Children.Add(openTasks);
        }
        card.Child = layout;
        card.MouseEnter += (_, _) => Hover(background, ((SolidColorBrush)Resource("Hover")).Color);
        card.MouseLeave += (_, _) => Hover(background, normal);
        return card;
    }

    private async Task InvokeAsync(NotificationEvent item, NotificationSubject subject, string action, Button button, TextBlock error)
    {
        if (ActionRequested is null) return;
        button.IsEnabled = false;
        try { await ActionRequested(item, subject, action); }
        catch (Exception ex) when (MainViewModel.IsStorageError(ex) || ex is ArgumentException)
        { error.Text = "Не удалось выполнить действие. Попробуйте ещё раз."; error.Visibility = Visibility.Visible; }
        finally { button.IsEnabled = true; }
    }

    private static string KindLabel(NotificationKind kind) => kind switch
    { NotificationKind.Personal => "НАПОМИНАНИЕ", NotificationKind.Signal => "ПРОСТОЙ СИГНАЛ", NotificationKind.Recurring => "ПОВТОРЯЮЩЕЕСЯ ДЕЛО", NotificationKind.CheckIn => "ВРЕМЯ ПРОВЕРИТЬ", NotificationKind.Missed => "ПРОПУСКИ", _ => "СВОДКА ЗАДАЧ" };
    private Brush Resource(string name) => (Brush)Window.FindResource(name);
    private static void Hover(SolidColorBrush brush, Color color)
    {
        if (SystemParameters.ClientAreaAnimation) brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(150)));
        else { brush.BeginAnimation(SolidColorBrush.ColorProperty, null); brush.Color = color; }
    }
    private void Position()
    {
        if (disposed || !Window.IsVisible || monitor == null) return;
        var work = monitor.WorkArea;
        var dpi = VisualTreeHelper.GetDpi(Window);
        var width = (int)Math.Ceiling(Window.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(Window.ActualHeight * dpi.DpiScaleY);
        var right = (int)Math.Round(22 * dpi.DpiScaleX);
        var bottom = (int)Math.Round(22 * dpi.DpiScaleY);
        SetWindowPos(new WindowInteropHelper(Window).Handle, -1, work.Right - right - width, work.Bottom - bottom - height, width, height, 0x0010);
    }
    public void PlaySound()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "notification.wav");
        if (!System.IO.File.Exists(path)) return;
        player.Volume = 0.18; player.Open(new Uri(path)); player.Play();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SystemParameters.StaticPropertyChanged -= ParametersChanged;
        player.Close(); Window.Close();
        VisibleEvents = [];
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
