using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Notifs.Core;

namespace Notifs.App;

public partial class MainWindow
{
    private DateOnly? rangeAnchor, rangePointer;
    private DateOnly? keyboardRangeStart, keyboardRangeEnd;
    private void PaintCalendarRange(DateOnly from, DateOnly to)
    {
        foreach (var (day, button) in dayButtons)
        {
            var selected = day >= (from < to ? from : to) && day <= (from > to ? from : to);
            button.Background = (Brush)FindResource(selected ? "Selected" : "Surface");
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, selected ? "Выбран" : "Не выбран");
        }
    }
    private async Task<bool> HandleRangeKeyAsync(DateOnly day, KeyEventArgs args)
    {
        if (args.Key == Key.Escape && keyboardRangeStart != null) { keyboardRangeStart = keyboardRangeEnd = null; RenderCalendar(); return true; }
        if (args.Key is Key.Enter or Key.Space && keyboardRangeStart is { } start && keyboardRangeEnd is { } end)
        { keyboardRangeStart = keyboardRangeEnd = null; await ShowCalendarPeriodAsync(new(start < end ? start : end, start > end ? start : end)); return true; }
        var offset = args.Key switch { Key.Left => -1, Key.Right => 1, Key.Up => -7, Key.Down => 7, _ => 0 };
        if (offset == 0 || !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return false;
        var next = day.AddDays(offset); if (!dayButtons.ContainsKey(next)) return true;
        keyboardRangeStart ??= day; keyboardRangeEnd = next; PaintCalendarRange(keyboardRangeStart.Value, next); FocusDay(next); return true;
    }
    private static string PeriodLabel(CalendarPeriod period) => period.Start == period.End ? FullDate(period.Start) :
        period.Start.ToString("d MMMM yyyy", CalendarCulture) + " — " + period.End.ToString("d MMMM yyyy", CalendarCulture);
    private void InitializeCalendarRange()
    {
        MonthHost.PreviewMouseMove += (_, args) =>
        {
            if (rangeAnchor == null || args.LeftButton != MouseButtonState.Pressed) return;
            foreach (var (day, button) in dayButtons)
            {
                var point = args.GetPosition(button);
                if (point.X >= 0 && point.Y >= 0 && point.X < button.ActualWidth && point.Y < button.ActualHeight) { rangePointer = day; break; }
            }
            var from = rangeAnchor.Value; var to = rangePointer ?? from;
            PaintCalendarRange(from, to);
        };
        MonthHost.PreviewMouseLeftButtonUp += async (_, args) =>
        {
            if (rangeAnchor is not { } start) return;
            var end = rangePointer ?? start; rangeAnchor = rangePointer = null; Mouse.Capture(null); args.Handled = true;
            await ShowCalendarPeriodAsync(new(start < end ? start : end, start > end ? start : end));
        };
        MonthHost.LostMouseCapture += (_, _) => { rangeAnchor = rangePointer = null; };
    }
    private void AddRangeFields(Panel header)
    {
        var fields = new WrapPanel { Margin = new(0, 12, 0, 12) };
        var start = new TextBox { Text = model.Calendar.Period.Start.ToString("dd.MM.yyyy"), Width = 130, Margin = new(0, 0, 12, 8) };
        var end = new TextBox { Text = model.Calendar.Period.End.ToString("dd.MM.yyyy"), Width = 130, Margin = new(0, 0, 12, 8) };
        System.Windows.Automation.AutomationProperties.SetName(start, "Начало периода ДД.ММ.ГГГГ");
        System.Windows.Automation.AutomationProperties.SetName(end, "Конец периода ДД.ММ.ГГГГ");
        var left = new StackPanel(); left.Children.Add(new Label { Content = "С", Target = start }); left.Children.Add(start); fields.Children.Add(left);
        var right = new StackPanel(); right.Children.Add(new Label { Content = "По", Target = end }); right.Children.Add(end); fields.Children.Add(right);
        var error = new TextBlock { Foreground = (Brush)FindResource("Danger"), FontSize = 12 };
        System.Windows.Automation.AutomationProperties.SetLiveSetting(error, System.Windows.Automation.AutomationLiveSetting.Assertive);
        var showPeriod = ActionButton("Показать период", async () =>
        {
            if (!DateOnly.TryParseExact(start.Text.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) || from.Year is < 2 or > 9998)
            { error.Text = "Начало: введите дату ДД.ММ.ГГГГ."; start.Focus(); return; }
            if (!DateOnly.TryParseExact(end.Text.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to) || to.Year is < 2 or > 9998 || to < from)
            { error.Text = "Конец: дата должна быть не раньше начала."; end.Focus(); return; }
            await ShowCalendarPeriodAsync(new(from, to));
        });
        showPeriod.Height = 44; showPeriod.VerticalAlignment = VerticalAlignment.Bottom; fields.Children.Add(showPeriod);
        header.Children.Add(fields); header.Children.Add(error);
    }
}
