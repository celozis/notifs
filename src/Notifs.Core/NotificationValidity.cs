namespace Notifs.Core;

public sealed partial class TaskService
{
    public NotificationEvent RefreshNotification(NotificationEvent notification)
    {
        if (notification.Kind == NotificationKind.Summary) return RefreshSummary(notification);
        if (notification.Kind == NotificationKind.Missed)
        {
            var tasks = List().Where(x => !IsSnoozed(x.Id)).Select(x => new EntityKey("task", x.Id));
            var signals = ListSignals().Where(x => x.Enabled && x.SnoozedUntil == null).SelectMany(x => x.Current is { } debt ? new[] { new EntityKey("recurring", debt.Id), new EntityKey("signal", x.Id) } : new[] { new EntityKey("signal", x.Id) });
            var eligible = tasks.Concat(signals).ToHashSet();
            return notification with { Subjects = notification.Subjects.Where(x => x.Origin is { } origin && origin != NotificationKind.Missed
                ? RefreshNotification(new(Guid.NewGuid(), origin, x.OriginId ?? "", [x])).Subjects.Count > 0 : eligible.Contains(x.Key)).ToArray() };
        }
        var valid = notification.Kind switch
        {
            NotificationKind.CheckIn => IsCheckInCurrent(notification),
            NotificationKind.Signal or NotificationKind.Recurring => IsSignalCurrent(notification),
            NotificationKind.Personal => List().Any(x => x.Id == notification.Subjects[0].Key.Id) && ReadPersonal(notification.Subjects[0].Key.Id) != null && !IsSnoozed(notification.Subjects[0].Key.Id),
            _ => true
        };
        return valid ? notification : notification with { Subjects = [] };
    }
}
