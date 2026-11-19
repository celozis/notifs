namespace Notifs.Core;

public enum SignalRepeat { Weekly, Hourly }

// Local calendar rules stay independent of process uptime and ordinary task reminders.
public sealed record SignalSchedule(DateOnly FirstDate, TimeOnly FirstTime, SignalRepeat Repeat,
    DayOfWeek[] Days, TimeOnly WeeklyTime, int HourlyMinute = 0)
{
    public void Validate(TimeZoneInfo zone)
    {
        if (!Enum.IsDefined(Repeat)) throw new ArgumentException("Выберите повторение по дням недели или каждый час.");
        if (Repeat == SignalRepeat.Weekly && (Days.Length == 0 || Days.Any(day => !Enum.IsDefined(day))))
            throw new ArgumentException("Выберите хотя бы один день недели.");
        if (Repeat == SignalRepeat.Hourly && HourlyMinute is < 0 or > 59)
            throw new ArgumentException("Минута часа: целое число от 0 до 59.");
        new TaskDeadline(FirstDate, FirstTime).Resolve(zone);
    }

    public DateTimeOffset? Next(DateTimeOffset boundary, TimeZoneInfo zone, bool inclusive = false)
    {
        var first = new TaskDeadline(FirstDate, FirstTime).Resolve(zone);
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
        Repeat == other.Repeat && (Repeat == SignalRepeat.Hourly ? HourlyMinute == other.HourlyMinute :
            WeeklyTime == other.WeeklyTime && Days.Order().SequenceEqual(other.Days.Order()));
}

public sealed record SignalReminder(Guid Id, string Title, string Description, SignalSchedule Schedule, bool Enabled,
    DateTimeOffset? NextAt, DateTimeOffset? SnoozedUntil, Guid Revision);
