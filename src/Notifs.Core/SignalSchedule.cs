namespace Notifs.Core;

public enum SignalRepeat { Weekly, Hourly, AfterCompletion }

// Local calendar rules stay independent of process uptime and ordinary task reminders.
public sealed record SignalSchedule(DateOnly FirstDate, TimeOnly FirstTime, SignalRepeat Repeat,
    DayOfWeek[] Days, TimeOnly WeeklyTime, int HourlyMinute = 0, int IntervalValue = 14, bool IntervalDays = true)
{
    public void Validate(TimeZoneInfo zone)
    {
        if (!Enum.IsDefined(Repeat)) throw new ArgumentException("Выберите способ повторения.");
        if (Repeat == SignalRepeat.AfterCompletion && IntervalValue is < 1 or > 36500)
            throw new ArgumentException("Интервал после выполнения: целое число от 1 до 36500.");
        if (Repeat == SignalRepeat.Weekly && (Days.Length == 0 || Days.Any(day => !Enum.IsDefined(day))))
            throw new ArgumentException("Выберите хотя бы один день недели.");
        if (Repeat == SignalRepeat.Hourly && HourlyMinute is < 0 or > 59)
            throw new ArgumentException("Минута часа: целое число от 0 до 59.");
        new TaskDeadline(FirstDate, FirstTime).Resolve(zone);
    }

    public DateTimeOffset? Next(DateTimeOffset boundary, TimeZoneInfo zone, bool inclusive = false)
    {
        var first = new TaskDeadline(FirstDate, FirstTime).Resolve(zone);
        if (Repeat == SignalRepeat.AfterCompletion) return inclusive ? first : null;
        var start = boundary > first ? boundary : first;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);
        // A weekly rule always has a candidate within eight dates; skip DST gaps.
        for (var offset = 0; offset < 15 && date.DayNumber + offset <= DateOnly.MaxValue.DayNumber; offset++)
        {
            var day = date.AddDays(offset);
            if (Repeat == SignalRepeat.Weekly && !Days.Contains(day.DayOfWeek)) continue;
            for (var hour = 0; hour < (Repeat == SignalRepeat.Hourly ? 24 : 1); hour++)
            {
                var local = day.ToDateTime(Repeat == SignalRepeat.Hourly ? new TimeOnly(hour, HourlyMinute) : WeeklyTime);
                if (zone.IsInvalidTime(local)) continue;
                // Ambiguous wall times use the same stable Windows offset as task deadlines.
                var instant = new DateTimeOffset(local, zone.GetUtcOffset(local));
                if (instant >= first && (inclusive ? instant >= boundary : instant > boundary)) return instant;
            }
        }
        return null;
    }

    internal bool SameRule(SignalSchedule other) => FirstDate == other.FirstDate && FirstTime == other.FirstTime &&
        Repeat == other.Repeat && (Repeat switch
        {
            SignalRepeat.Hourly => HourlyMinute == other.HourlyMinute,
            SignalRepeat.AfterCompletion => IntervalValue == other.IntervalValue && IntervalDays == other.IntervalDays,
            _ => WeeklyTime == other.WeeklyTime && Days.Order().SequenceEqual(other.Days.Order())
        });

    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan CompletionInterval => IntervalDays ? TimeSpan.FromDays(IntervalValue) : TimeSpan.FromHours(IntervalValue);
}

public sealed record RecurringOccurrence(Guid Id, DateTimeOffset DueAt, DateTimeOffset NotifyAt);
public sealed record ReminderCompletion(Guid OccurrenceId, DateTimeOffset DueAt, DateTimeOffset CompletedAt);
public sealed record SignalReminder(Guid Id, string Title, string Description, SignalSchedule Schedule, bool Enabled,
    DateTimeOffset? NextAt, DateTimeOffset? SnoozedUntil, Guid Revision, bool RequiresCompletion = false,
    int RepeatMinutes = 60, RecurringOccurrence? Current = null, ReminderCompletion[]? Completions = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ReminderCompletion> History => Completions ?? [];
}
