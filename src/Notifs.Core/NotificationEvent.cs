namespace Notifs.Core;

public enum NotificationKind { Personal, Signal, Recurring, CheckIn, Summary }
public sealed record EntityKey(string Type, Guid Id);
public sealed record NotificationSubject(EntityKey Key, string Title, string Detail);
public sealed record NotificationAction(string Id, string Label);
public sealed record NotificationEvent(Guid Id, NotificationKind Kind, string SourceId, IReadOnlyList<NotificationSubject> Subjects,
    IReadOnlyList<NotificationAction>? Actions = null);
