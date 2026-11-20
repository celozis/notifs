using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Notifs.Core;

namespace Notifs.App;

/// <summary>A draft of one section's settings. Closing the panel never writes it.</summary>
public sealed class TaskFiltersView : UserControl
{
    private readonly TaskSection section;
    private readonly Func<TaskListSettings, Task<bool>> apply;
    private readonly ComboBox sort = Choice("Порядок", "По срочности", "По рангу", "По полной оценке", "По дедлайну", "По созданию");
    private readonly ComboBox direction = Choice("Направление", "По возрастанию", "По убыванию");
    private readonly ComboBox status = Choice("Состояние", "Все", "Не сделаны", "Сделаны");
    private readonly ComboBox estimateMode = Choice("Полная оценка", "Любая / диапазон", "Без оценки");
    private readonly ComboBox deadlineMode = Choice("Дедлайн", "Любой / диапазон", "Без дедлайна");
    private readonly ComboBox unit = Choice("Единицы оценки", "Минуты", "Часы");
    private readonly TextBox estimateFrom = Input("Оценка от"), estimateTo = Input("Оценка до"), deadlineFrom = Input("Дедлайн с"), deadlineTo = Input("Дедлайн по"), createdFrom = Input("Создана с"), createdTo = Input("Создана по");
    private readonly CheckBox[] ranks = Enum.GetValues<ImportanceRank>().Select(rank => new CheckBox { Content = new RankBadge { Rank = rank }, Tag = rank, Margin = new(0, 0, 16, 0) }).ToArray();
    private readonly TextBlock error = new() { FontSize = 13, Margin = new(0, 10, 0, 0) };
    private readonly List<UniformPair> pairs = [];
    private readonly Button save;
    public event Action? CloseRequested;
    public bool IsBusy { get; private set; }

    public TaskFiltersView(TaskSection section, TaskListSettings initial, Func<TaskListSettings, Task<bool>> apply)
    {
        this.section = section; this.apply = apply;
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new(0, 0, 0, 18) };
        var close = new Button { Content = "×", Width = 44, Padding = new(0) }; AutomationProperties.SetName(close, "Закрыть фильтры"); DockPanel.SetDock(close, Dock.Right);
        close.Click += (_, _) => { if (!IsBusy) CloseRequested?.Invoke(); }; header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = "Фильтры и сортировка", FontSize = 22, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); root.Children.Add(header);
        var body = new StackPanel(); var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        void Pair(string a, FrameworkElement x, string b, FrameworkElement y) { var pair = new UniformPair(Field(a, x), Field(b, y)); pairs.Add(pair); body.Children.Add(pair); }
        Pair("Порядок", sort, "Направление", direction);
        body.Children.Add(Field("Состояние", status));
        var rankPanel = new WrapPanel(); foreach (var rank in ranks) { rankPanel.Children.Add(rank); AutomationProperties.SetName(rank, "Ранг " + rank.Tag); }
        body.Children.Add(Field("Ранг · можно выбрать несколько", rankPanel));
        Pair("Полная оценка", estimateMode, "Единицы", unit);
        Pair("От", estimateFrom, "До", estimateTo);
        body.Children.Add(Field("Дедлайн", deadlineMode));
        Pair("Дедлайн с · ДД.ММ.ГГГГ", deadlineFrom, "По · ДД.ММ.ГГГГ", deadlineTo);
        Pair("Создана с · ДД.ММ.ГГГГ", createdFrom, "По · ДД.ММ.ГГГГ", createdTo);
        error.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        var footer = new StackPanel(); footer.Children.Add(error);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        var reset = new Button { Content = "Сбросить", Margin = new(0, 0, 8, 8) }; reset.Click += (_, _) => { if (!IsBusy) Populate(TaskListSettings.Default(section)); };
        save = new Button { Content = "Применить", Style = (Style)FindResource("PrimaryButton"), Margin = new(0, 0, 0, 8) };
        save.Click += async (_, _) =>
        {
            try
            {
                var settings = Read(); settings.Validate(); IsBusy = true; IsEnabled = false;
                if (await apply(settings)) { IsBusy = false; CloseRequested?.Invoke(); } else error.Text = "Не удалось записать настройки. Ввод сохранён в панели; повторите применение.";
            }
            catch (ArgumentException ex) { error.Text = ex.Message; }
            finally { IsBusy = false; IsEnabled = true; }
        };
        actions.Children.Add(reset); actions.Children.Add(save); footer.Children.Add(actions); Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        sort.SelectionChanged += (_, _) => UpdateDirection();
        estimateMode.SelectionChanged += (_, _) => estimateFrom.IsEnabled = estimateTo.IsEnabled = unit.IsEnabled = estimateMode.SelectedIndex == 0;
        deadlineMode.SelectionChanged += (_, _) => deadlineFrom.IsEnabled = deadlineTo.IsEnabled = deadlineMode.SelectedIndex == 0;
        foreach (var box in new[] { sort, direction, status, estimateMode, deadlineMode, unit }) box.Style = (Style)FindResource("FilterChoice");
        Populate(initial);
        UpdateDirection();
        SizeChanged += (_, _) => { foreach (var pair in pairs) pair.Stacked = ActualWidth < 430; };
        Loaded += (_, _) => sort.Focus();
    }

    private void Populate(TaskListSettings value)
    {
        sort.SelectedIndex = (int)value.Sort; direction.SelectedIndex = value.Descending ? 1 : 0; status.SelectedIndex = (int)value.Status;
        foreach (var rank in ranks) rank.IsChecked = value.Ranks.Contains((ImportanceRank)rank.Tag);
        estimateMode.SelectedIndex = (int)value.EstimateMode; deadlineMode.SelectedIndex = (int)value.DeadlineMode; unit.SelectedIndex = 0;
        estimateFrom.Text = value.EstimateFrom?.ToString() ?? ""; estimateTo.Text = value.EstimateTo?.ToString() ?? "";
        deadlineFrom.Text = Date(value.DeadlineFrom); deadlineTo.Text = Date(value.DeadlineTo); createdFrom.Text = Date(value.CreatedFrom); createdTo.Text = Date(value.CreatedTo); error.Text = "";
    }
    private void UpdateDirection()
    {
        var descending = direction.SelectedIndex == 1;
        direction.ItemsSource = sort.SelectedIndex == 0 ? new[] { "Автоматически", "Автоматически" } : new[] { "По возрастанию", "По убыванию" };
        direction.SelectedIndex = descending ? 1 : 0; direction.IsEnabled = sort.SelectedIndex != 0;
    }
    private static string Date(DateOnly? date) => date?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "";
    private TaskListSettings Read() => new((TaskStatusFilter)status.SelectedIndex, (TaskSort)sort.SelectedIndex, direction.SelectedIndex == 1,
        (OptionalFieldFilter)estimateMode.SelectedIndex, estimateMode.SelectedIndex == 1 ? null : Minutes(estimateFrom), estimateMode.SelectedIndex == 1 ? null : Minutes(estimateTo),
        (OptionalFieldFilter)deadlineMode.SelectedIndex, deadlineMode.SelectedIndex == 1 ? null : ParseDate(deadlineFrom), deadlineMode.SelectedIndex == 1 ? null : ParseDate(deadlineTo), ParseDate(createdFrom), ParseDate(createdTo))
        { Ranks = ranks.Where(x => x.IsChecked == true).Select(x => (ImportanceRank)x.Tag).ToArray() };
    private int? Minutes(TextBox input)
    {
        if (string.IsNullOrWhiteSpace(input.Text)) return null;
        if (int.TryParse(input.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 && (long)number * (unit.SelectedIndex == 1 ? 60 : 1) <= int.MaxValue)
            return number * (unit.SelectedIndex == 1 ? 60 : 1);
        input.Focus(); throw new ArgumentException("Введите положительную оценку целым числом.");
    }
    private static DateOnly? ParseDate(TextBox input)
    {
        if (string.IsNullOrWhiteSpace(input.Text)) return null;
        if (DateOnly.TryParseExact(input.Text.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        input.Focus(); throw new ArgumentException("Введите существующую дату: ДД.ММ.ГГГГ.");
    }
    private static ComboBox Choice(string name, params string[] options)
    {
        var box = new ComboBox { ItemsSource = options, SelectedIndex = 0, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(box, name); return box;
    }
    private static TextBox Input(string name) { var box = new TextBox { MinWidth = 0 }; AutomationProperties.SetName(box, name); return box; }
    private static StackPanel Field(string title, FrameworkElement input)
    {
        var panel = new StackPanel { Margin = new(0, 0, 0, 16) }; panel.Children.Add(new TextBlock { Text = title, FontSize = 13, Margin = new(0, 0, 0, 8) }); panel.Children.Add(input); return panel;
    }
    private sealed class UniformPair : Grid
    {
        private readonly FrameworkElement second;
        public UniformPair(FrameworkElement first, FrameworkElement second)
        {
            this.second = second; ColumnDefinitions.Add(new()); ColumnDefinitions.Add(new() { Width = new(16) }); ColumnDefinitions.Add(new()); RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto });
            Children.Add(first); Children.Add(second); Stacked = false;
        }
        public bool Stacked { set { ColumnDefinitions[1].Width = new(value ? 0 : 16); ColumnDefinitions[2].Width = value ? new(0) : new(1, GridUnitType.Star); SetRow(second, value ? 1 : 0); SetColumn(second, value ? 0 : 2); } }
    }
}
