using System.Text.Json;

namespace Notifs.Core;

[Flags]
public enum CalendarReason { None = 0, Created = 1, Deadline = 2, EstimatedSpan = 4, Started = 8, CheckIn = 16 }
public sealed record CalendarTask(TaskItem Task, CalendarReason Reasons);
public sealed record CalendarPeriod(DateOnly Start, DateOnly End)
{
    public bool Contains(DateOnly day) => day >= Start && day <= End;
}
public sealed record CalendarState(DateOnly Month, DateOnly SelectedDay, bool WholeWeek = false, bool IsMonth = false, DateOnly? RangeStart = null, DateOnly? RangeEnd = null)
{
    public static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    public CalendarPeriod Period => RangeStart is { } from && RangeEnd is { } to ? from <= to ? new(from, to) : new(to, from) :
        WholeWeek ? new(Monday(SelectedDay), Monday(SelectedDay).AddDays(6)) : new(SelectedDay, SelectedDay);
}

public sealed partial class TaskService
{
    public DateOnly Today => LocalDate(time.GetUtcNow());
    public DateTimeOffset? EstimatedCompletion(TaskItem item)
    {
        if (item.Deadline != null || item.EstimateMinutes is not { } estimate) return null;
        var ticks = Math.Min(TimeSpan.FromMinutes(estimate).Ticks, DateTimeOffset.MaxValue.UtcTicks - item.CreatedAt.UtcTicks);
        return item.CreatedAt.AddTicks(ticks);
    }
    private DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, time.LocalTimeZone).DateTime);
    public CalendarState ReadCalendarState()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM calendar_state WHERE slot=1";
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<CalendarState>(json)! : new(new(Today.Year, Today.Month, 1), Today);
    }
    public void SaveCalendarState(CalendarState state)
    {
        // Leave room for the complete weeks and neighbouring dates at the edges of the grid.
        if (state.Month.Year is < 2 or > 9998 || state.Month.Day != 1 || state.SelectedDay.Year is < 2 or > 9998) throw new ArgumentException("Выберите дату между 0002 и 9998 годами.");
        if ((state.RangeStart == null) != (state.RangeEnd == null) || state.Period.Start.Year < 2 || state.Period.End.Year > 9998)
            throw new ArgumentException("Укажите обе границы периода между 0002 и 9998 годами.");
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO calendar_state(slot,data) VALUES(1,$data) ON CONFLICT(slot) DO UPDATE SET data=excluded.data";
        command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(state)); command.ExecuteNonQuery();
    }
    public IReadOnlyList<CalendarTask> QueryCalendar(CalendarPeriod period, TaskListSettings settings)
    {
        if (period.Start > period.End) throw new ArgumentException("Начало периода должно быть не позже конца.");
        return ProjectCalendar(ApplyTaskList(ListAll(), settings), period);
    }
    public IReadOnlyList<CalendarTask> ProjectCalendar(IEnumerable<TaskItem> source, CalendarPeriod period)
    {
        var sessions = ReadSessions().ToDictionary(x => x.TaskId); var now = time.GetUtcNow();
        return source.Select(item => new CalendarTask(item, ReasonsFor(item, period, sessions.GetValueOrDefault(item.Id), now))).Where(x => x.Reasons != CalendarReason.None).ToArray();
    }
    public CalendarReason ReasonsFor(TaskItem item, CalendarPeriod period) => ReasonsFor(item, period, ReadSession(item.Id), time.GetUtcNow());
    private CalendarReason ReasonsFor(TaskItem item, CalendarPeriod period, TaskSession? session, DateTimeOffset now)
    {
        var created = LocalDate(item.CreatedAt);
        var reasons = period.Contains(created) ? CalendarReason.Created : CalendarReason.None;
        if (item.Deadline is { } deadline && period.Contains(deadline.Date)) reasons |= CalendarReason.Deadline;
        if (session != null && period.Contains(LocalDate(session.StartedAt))) reasons |= CalendarReason.Started;
        if (CheckInAt(session, now) is { } question && period.Contains(LocalDate(question))) reasons |= CalendarReason.CheckIn;
        if (EstimatedCompletion(item) is { } estimatedEnd)
        {
            var end = LocalDate(estimatedEnd);
            if (created <= period.End && end >= period.Start) reasons |= CalendarReason.EstimatedSpan;
        }
        return reasons;
    }
}
