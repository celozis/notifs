using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using Notifs.Core;

namespace Notifs.App;

public partial class MainWindow
{
    public AsyncCommand CalendarCommand { get; private set; } = null!;
    private Button? calendarNavigation;
    private readonly Dictionary<DateOnly, Button> dayButtons = [];
    private DateOnly? renderedMonth;
    private bool renderedMonthMode;
    private static readonly CultureInfo CalendarCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static string FullDate(DateOnly date) => date.ToString("dddd, d MMMM yyyy", CalendarCulture);
    private void InitializeCalendar()
    {
        CalendarCommand = new(async () => { if (editor?.PersistDraft() == false) return; CloseEditor(); showingDetails = false; await model.OpenCalendarAsync(); AdaptLayout(); }, CanNavigate);
        model.CalendarChanged += RenderCalendar;
        InitializeCalendarRange();
    }
    private Button ActionButton(string text, Func<Task> action, string? name = null)
    {
        var button = new Button { Content = text, FontSize = 12, Margin = new(0, 0, 6, 8), Command = new AsyncCommand(action, () => !model.IsBusy) };
        AutomationProperties.SetName(button, name ?? text); return button;
    }
    private async Task MoveCalendarAsync(CalendarState next, DateOnly? focus = null)
    {
        if (!CanNavigate()) return;
        if (await model.ChangeCalendarAsync(next) && focus is { } day) await Dispatcher.InvokeAsync(() => FocusDay(day), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
    private void FocusDay(DateOnly day)
    {
        if (dayButtons.TryGetValue(day, out var button)) { button.BringIntoView(); button.Focus(); }
    }
    private void RenderCalendar()
    {
        CalendarHeader.Visibility = model.IsCalendar ? Visibility.Visible : Visibility.Collapsed;
        MonthWeekdays.Visibility = model.IsCalendar && model.Calendar.IsMonth ? Visibility.Visible : Visibility.Collapsed;
        MonthHost.Visibility = model.IsCalendar && model.Calendar.IsMonth ? Visibility.Visible : Visibility.Collapsed;
        ContentGrid.Visibility = model.IsCalendar && model.Calendar.IsMonth ? Visibility.Collapsed : Visibility.Visible;
        if (calendarNavigation != null) calendarNavigation.Background = (Brush)FindResource(model.IsCalendar ? "Selected" : "Navigation");
        if (!model.IsCalendar) return;
        var state = model.Calendar; dayButtons.Clear();
        var resetScroll = !renderedMonthMode || renderedMonth != state.Month;
        renderedMonthMode = state.IsMonth; renderedMonth = state.Month;
        var header = new StackPanel();
        var toolbar = new WrapPanel { Margin = new(0, 14, 0, 0) };
        toolbar.Children.Add(ActionButton("Сегодня", async () => { var today = model.Service.Today; await MoveCalendarAsync(new(new(today.Year, today.Month, 1), today), today); }));
        foreach (var delta in new[] { -1, 1 })
        {
            var step = delta;
            var button = ActionButton(step < 0 ? "‹" : "›", async () =>
            {
                var next = state.IsMonth ? state with { Month = state.Month.AddMonths(step) } : state with { SelectedDay = state.SelectedDay.AddDays(step * 7) };
                if (!state.IsMonth) next = next with { Month = new(next.SelectedDay.Year, next.SelectedDay.Month, 1) };
                var focus = state.IsMonth ? next.Month.AddDays(Math.Min(state.SelectedDay.Day, DateTime.DaysInMonth(next.Month.Year, next.Month.Month)) - 1) : next.SelectedDay;
                await MoveCalendarAsync(next, focus);
            }, (step < 0 ? "Предыдущий" : "Следующий") + (state.IsMonth ? " месяц" : " неделя"));
            button.MinWidth = 40; button.IsEnabled = (step > 0 || (state.IsMonth ? state.Month : state.SelectedDay) > new DateOnly(2, 1, 8)) && (step < 0 || (state.IsMonth ? state.Month : state.SelectedDay) < new DateOnly(9998, 12, 24)); toolbar.Children.Add(button);
        }
        var monday = CalendarState.Monday(state.SelectedDay);
        toolbar.Children.Add(new TextBlock { Text = state.IsMonth ? state.Month.ToString("MMMM yyyy 'г.'", CalendarCulture) : monday.ToString("d MMMM", CalendarCulture) + " — " + monday.AddDays(6).ToString("d MMMM yyyy", CalendarCulture), FontSize = state.IsMonth ? 22 : 13, Margin = new(10, 8, 16, 8), VerticalAlignment = VerticalAlignment.Center });
        toolbar.Children.Add(ActionButton(state.IsMonth ? "К неделе" : "Открыть календарь", async () => await MoveCalendarAsync(state with { IsMonth = !state.IsMonth }, state.SelectedDay)));
        header.Children.Add(toolbar);
        AddRangeFields(header);
        if (!state.IsMonth)
        {
            var week = new UniformGrid { Columns = 7, Margin = new(0, 6, 0, 8) };
            for (var index = 0; index < 7; index++)
            {
                var date = monday.AddDays(index); var day = DayButton(date, false); day.Height = 76; week.Children.Add(day);
            }
            header.Children.Add(week);
            var selection = new WrapPanel { Margin = new(0, 6, 0, 0) };
            selection.Children.Add(new TextBlock { Text = state.RangeStart != null ? PeriodLabel(state.Period) : state.WholeWeek ? "Вся неделя · " + monday.ToString("d MMMM", CalendarCulture) + " — " + monday.AddDays(6).ToString("d MMMM", CalendarCulture) : state.SelectedDay.ToString("d MMMM yyyy", CalendarCulture), FontSize = 13, Margin = new(0, 8, 16, 8), VerticalAlignment = VerticalAlignment.Center });
            selection.Children.Add(ActionButton("Вся неделя", async () => await MoveCalendarAsync(state with { WholeWeek = true, RangeStart = null, RangeEnd = null })));
            header.Children.Add(selection);
        }
        CalendarHeader.Content = header;
        if (!state.IsMonth) return;
        var month = new StackPanel();
        var grid = new UniformGrid { Columns = 7 };
        var start = CalendarState.Monday(state.Month);
        var last = state.Month.AddMonths(1).AddDays(-1); var cells = ((last.DayNumber - start.DayNumber) / 7 + 1) * 7;
        for (var index = 0; index < cells; index++) grid.Children.Add(DayButton(start.AddDays(index), true));
        month.Children.Add(grid); month.Children.Add(new TextBlock { Text = state.SelectedDay.ToString("d MMMM yyyy", CalendarCulture), FontSize = 13, Margin = new(0, 14, 0, 8), Foreground = (Brush)FindResource("Primary") });
        MonthHost.Content = month;
        if (resetScroll) MonthHost.ScrollToTop();
    }
    private Button DayButton(DateOnly date, bool month)
    {
        var state = model.Calendar; var entries = model.CalendarEntries(new(date, date));
        var selected = state.Period.Contains(date);
        var today = date == model.Service.Today;
        var button = new Button { Tag = date, Margin = new(0, 0, 6, 6), Padding = new(month ? 5 : 2), Height = month ? 118 : 76, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, MinWidth = 0, MinHeight = month ? 118 : 76,
            Background = (Brush)FindResource(selected ? month ? "Selected" : "Primary" : "Surface"), Foreground = (Brush)FindResource(selected && !month ? "OnPrimary" : date.Month == state.Month.Month || !month ? "Ink" : "MutedInk"), BorderBrush = (Brush)FindResource(selected || today ? "Primary" : "Line"), BorderThickness = new(today || selected ? 2 : 1) };
        AutomationProperties.SetName(button, FullDate(date) + (selected ? ", выбран" : "") + (today ? ", сегодня" : "") + $", задач: {entries.Count}"); AutomationProperties.SetItemStatus(button, selected ? "Выбран" : "Не выбран");
        var content = new StackPanel();
        if (!month) content.Children.Add(new TextBlock { Text = date.ToString("ddd", CalendarCulture).ToUpperInvariant(), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 6, 0, 5) });
        var number = new TextBlock { Text = date.Day.ToString(), FontSize = month ? 12 : 20, LineHeight = month ? 16 : 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource(today && month ? "OnPrimary" : selected && !month ? "OnPrimary" : "Ink") };
        var numberSurface = new Border { Child = number, Width = month ? 24 : double.NaN, Height = month ? 24 : 28, CornerRadius = new(12), Background = today && month ? (Brush)FindResource("Primary") : Brushes.Transparent, HorizontalAlignment = month ? HorizontalAlignment.Right : HorizontalAlignment.Stretch, Margin = new(0, 0, 0, 4) };
        content.Children.Add(numberSurface);
        button.SizeChanged += (_, _) =>
        {
            // Use the allocated calendar width; measuring the button itself would feed its margin back into this breakpoint.
            var compact = CalendarHeader.ActualWidth < 420;
            button.Padding = new(compact ? 1 : month ? 5 : 2);
            button.Margin = new(0, 0, compact ? 2 : 6, 6);
            if (month) numberSurface.Width = compact ? 16 : 24;
            else number.FontSize = compact ? 14 : 20;
        };
        if (month)
        {
            foreach (var entry in entries.Take(3))
            {
                var line = new Grid { Margin = new(0, 0, 0, 3), Height = 19 }; line.ColumnDefinitions.Add(new() { Width = new(19) }); line.ColumnDefinitions.Add(new());
                if (entry.Task is { } ordinary) line.Children.Add(new RankBadge { Rank = ordinary.Rank, Width = 18, Height = 19 }); else line.Children.Add(new TextBlock { Text = "↻", FontSize = 14, Foreground = (Brush)FindResource("Primary") });
                var title = new TextBlock { Text = (entry.IsCompleted ? "✓ " : "") + entry.Title, FontSize = 10, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0, 0, 0), Foreground = (Brush)FindResource(entry.IsCompleted ? "MutedInk" : "Ink"), TextDecorations = entry.IsCompleted ? TextDecorations.Strikethrough : null }; Grid.SetColumn(title, 1); line.Children.Add(title); content.Children.Add(line);
            }
            if (entries.Count > 3) content.Children.Add(new TextBlock { Text = $"Ещё {entries.Count - 3}", FontSize = 10, LineHeight = 14, Foreground = (Brush)FindResource("MutedInk") });
        }
        if (month) button.PreviewMouseLeftButtonDown += (_, args) => { if (!CanNavigate()) return; rangeAnchor = rangePointer = date; Mouse.Capture(MonthHost, CaptureMode.SubTree); args.Handled = true; };
        button.Content = content; button.Command = new AsyncCommand(async () => { if (month) await ShowCalendarDayAsync(date); else await MoveCalendarAsync(state with { SelectedDay = date, WholeWeek = false, RangeStart = null, RangeEnd = null }, date); }, CanNavigate);
        button.PreviewKeyDown += async (_, args) =>
        {
            if (month && await HandleRangeKeyAsync(date, args)) { args.Handled = true; return; }
            var offset = args.Key switch { Key.Left => -1, Key.Right => 1, Key.Up => -7, Key.Down => 7, _ => 0 };
            if (offset == 0) return; args.Handled = true;
            var next = date.AddDays(offset); if (next.Year is < 2 or > 9998) return;
            if (month && dayButtons.ContainsKey(next)) FocusDay(next);
            else await MoveCalendarAsync(state with { SelectedDay = next, WholeWeek = false, Month = new(next.Year, next.Month, 1) }, next);
        };
        dayButtons[date] = button; return button;
    }
    public Task ShowCalendarDayAsync(DateOnly day) => ShowCalendarPeriodAsync(new(day, day));
    public async Task ShowCalendarPeriodAsync(CalendarPeriod period)
    {
        var day = period.Start;
        if (!CanNavigate()) return;
        if (!await model.ChangeCalendarAsync(model.Calendar with { SelectedDay = day, WholeWeek = false, RangeStart = period.Start == period.End ? null : period.Start, RangeEnd = period.Start == period.End ? null : period.End })) return;
        FocusDay(day);
        var panel = new Grid(); panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var title = new DockPanel { Margin = new(0, 0, 0, 18) };
        var close = ActionButton("×", () => { CloseTaskPanel(); return Task.CompletedTask; }, "Закрыть день"); DockPanel.SetDock(close, Dock.Right); title.Children.Add(close);
        title.Children.Add(new TextBlock { Text = PeriodLabel(period), FontSize = 22, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); panel.Children.Add(title);
        var rows = new StackPanel(); var entries = model.CalendarEntries(period);
        if (entries.Count == 0) rows.Children.Add(new TextBlock { Text = "В этом периоде нет задач с текущими фильтрами.", FontSize = 14, Margin = new(0, 16, 0, 16) });
        foreach (var entry in entries)
        {
            var row = new StackPanel(); var name = new TextBlock { Text = (entry.IsCompleted ? "✓ " : "") + entry.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextDecorations = entry.IsCompleted ? TextDecorations.Strikethrough : null }; row.Children.Add(name);
            var metadata = new WrapPanel { Margin = new(0, 10, 0, 8) }; if (entry.Task is { } ordinary) metadata.Children.Add(new RankBadge { Rank = ordinary.Rank }); metadata.Children.Add(new TextBlock { Text = entry.Task != null ? new TaskRow(entry.Task, model.Service).MetadataLabel : new CalendarReminderRow(entry, model.Service).MetadataLabel, FontSize = 11, Margin = new(8, 4, 0, 0) }); row.Children.Add(metadata);
            row.Children.Add(new TextBlock { Text = entry.Reasons, FontSize = 11, Foreground = (Brush)FindResource("MutedInk") });
            var task = new Button { Content = row, Padding = new(16), HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new(0, 0, 0, 10) };
            AutomationProperties.SetName(task, entry.Title + ", " + entry.Reasons);
            task.Command = new AsyncCommand(() => entry.Reminder is { } reminder ? OpenCalendarReminderAsync(reminder.ScheduleId) : GoToCalendarDayAsync(day, entry.Task!.Id), () => !model.IsBusy); rows.Children.Add(task);
        }
        var scroll = new ScrollViewer { Content = rows, MaxHeight = Math.Max(60, Math.Min(600, Shell.ActualHeight - 220)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); panel.Children.Add(scroll);
        var go = ActionButton(period.Start == period.End ? "Перейти к этому дню" : "Перейти к этому периоду", async () => await GoToCalendarDayAsync(day)); go.Style = (Style)FindResource("PrimaryButton"); go.HorizontalAlignment = HorizontalAlignment.Right; go.Margin = new(0, 16, 0, 0); Grid.SetRow(go, 2); panel.Children.Add(go);
        ShowTaskPanel(panel, () => FocusDay(day)); close.Focus();
    }
    private async Task GoToCalendarDayAsync(DateOnly day, Guid? task = null)
    {
        if (model.IsBusy) return;
        CloseTaskPanel();
        if (!await model.ChangeCalendarAsync(model.Calendar with { Month = new(day.Year, day.Month, 1), SelectedDay = day, IsMonth = false })) return;
        if (task != null) { model.Selected = model.Tasks.FirstOrDefault(x => x.Item.Id == task); showingDetails = true; }
        else showingDetails = false;
        AdaptLayout(); await Dispatcher.InvokeAsync(() => { if (task == null) FocusDay(day); else FocusSelected(); }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
    public static string ReasonLabel(CalendarReason reasons) => TaskService.CalendarReasonText(reasons);
}
