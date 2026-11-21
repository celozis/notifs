namespace Notifs.Core;

public enum NotificationKind { Personal, Signal, Recurring, CheckIn, Summary, Missed }
public sealed record EntityKey(string Type, Guid Id);
public sealed record NotificationSubject(EntityKey Key, string Title, string Detail, ImportanceRank? Rank = null, NotificationKind? Origin = null, string? OriginId = null);
public sealed record NotificationAction(string Id, string Label);
public sealed record NotificationEvent(Guid Id, NotificationKind Kind, string SourceId, IReadOnlyList<NotificationSubject> Subjects,
    IReadOnlyList<NotificationAction>? Actions = null);
