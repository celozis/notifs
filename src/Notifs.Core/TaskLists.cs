using System.Text.Json;

namespace Notifs.Core;

public enum TaskSection { Tasks, InProgress, History, Calendar }
public enum TaskStatusFilter { All, Incomplete, Completed }
public enum TaskSort { Urgency, Rank, Estimate, Deadline, Created }
public enum OptionalFieldFilter { Any, Missing }

public sealed record TaskListSettings(
    TaskStatusFilter Status = TaskStatusFilter.All, TaskSort Sort = TaskSort.Urgency, bool Descending = false,
    OptionalFieldFilter EstimateMode = OptionalFieldFilter.Any, int? EstimateFrom = null, int? EstimateTo = null,
    OptionalFieldFilter DeadlineMode = OptionalFieldFilter.Any, DateOnly? DeadlineFrom = null, DateOnly? DeadlineTo = null,
    DateOnly? CreatedFrom = null, DateOnly? CreatedTo = null)
{
    public ImportanceRank[] Ranks { get; init; } = [];
    public static TaskListSettings Default(TaskSection section) => new(Status: section == TaskSection.History ? TaskStatusFilter.Completed : TaskStatusFilter.All);
    public bool HasFieldFilters => Ranks.Length > 0 || EstimateMode != OptionalFieldFilter.Any || EstimateFrom != null || EstimateTo != null ||
        DeadlineMode != OptionalFieldFilter.Any || DeadlineFrom != null || DeadlineTo != null || CreatedFrom != null || CreatedTo != null;

    public void Validate()
    {
        if (!Enum.IsDefined(Status) || !Enum.IsDefined(Sort) || !Enum.IsDefined(EstimateMode) || !Enum.IsDefined(DeadlineMode) || Ranks.Any(x => !Enum.IsDefined(x)))
            throw new ArgumentException("Выберите существующее значение фильтра.");
        if (EstimateFrom is <= 0 || EstimateTo is <= 0 || EstimateFrom > EstimateTo)
            throw new ArgumentException("Оценка должна быть положительной; начало не больше конца.");
        if (DeadlineFrom > DeadlineTo || CreatedFrom > CreatedTo) throw new ArgumentException("Начало диапазона дат не должно быть позже конца.");
    }
}

public sealed partial class TaskService
{
    public TaskListSettings ReadTaskListSettings(TaskSection section)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM task_list_settings WHERE section=$section";
        command.Parameters.AddWithValue("$section", section.ToString());
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<TaskListSettings>(json)! : TaskListSettings.Default(section);
    }

    public void SaveTaskListSettings(TaskSection section, TaskListSettings settings)
    {
        settings.Validate();
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO task_list_settings(section,data) VALUES($section,$data) ON CONFLICT(section) DO UPDATE SET data=excluded.data";
        command.Parameters.AddWithValue("$section", section.ToString()); command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(settings)); command.ExecuteNonQuery();
    }

    public IReadOnlyList<TaskItem> QueryTasks(TaskListSettings settings) => ApplyTaskList(ListAll(), settings);

    public IReadOnlyList<TaskItem> ApplyTaskList(IEnumerable<TaskItem> source, TaskListSettings settings)
    {
        settings.Validate();
        var matches = source.Where(item => MatchesTaskList(item, settings));
        return OrderTaskList(matches, settings);
    }

    public IReadOnlyList<TaskItem> OrderTaskList(IEnumerable<TaskItem> matches, TaskListSettings settings)
    {
        if (settings.Sort == TaskSort.Urgency) return Order(matches);
        IComparable? Key(TaskItem item) => settings.Sort switch
        {
            TaskSort.Rank => (int)item.Rank, TaskSort.Estimate => item.EstimateMinutes,
            TaskSort.Deadline => item.Deadline?.Resolve(time.LocalTimeZone), _ => item.CreatedAt
        };
        var groups = matches.OrderBy(x => x.CompletedAt != null).ThenBy(x => Key(x) == null);
        var ordered = settings.Descending ? groups.ThenByDescending(Key) : groups.ThenBy(Key);
        return ordered.ThenBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArray();
    }

    private bool MatchesTaskList(TaskItem item, TaskListSettings settings)
    {
        var created = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.CreatedAt, time.LocalTimeZone).DateTime);
        return (settings.Status == TaskStatusFilter.All || (item.CompletedAt != null) == (settings.Status == TaskStatusFilter.Completed)) &&
            (settings.Ranks.Length == 0 || settings.Ranks.Contains(item.Rank)) &&
            (settings.EstimateMode == OptionalFieldFilter.Missing ? item.EstimateMinutes == null :
                (settings.EstimateFrom == null || item.EstimateMinutes >= settings.EstimateFrom) && (settings.EstimateTo == null || item.EstimateMinutes <= settings.EstimateTo)) &&
            (settings.DeadlineMode == OptionalFieldFilter.Missing ? item.Deadline == null :
                (settings.DeadlineFrom == null || item.Deadline?.Date >= settings.DeadlineFrom) && (settings.DeadlineTo == null || item.Deadline?.Date <= settings.DeadlineTo)) &&
            (settings.CreatedFrom == null || created >= settings.CreatedFrom) && (settings.CreatedTo == null || created <= settings.CreatedTo);
    }
}
