namespace Notifs.Core;

public interface INotificationOutput
{
    void Render(IReadOnlyList<NotificationEvent> events);
    void PlaySound();
}

/// <summary>Shared, source-independent visible stack. All operations run on its owning dispatcher.</summary>
public sealed class NotificationDelivery(INotificationOutput output, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly List<NotificationEvent> visible = [];
    private readonly List<NotificationEvent> waiting = [];
    private bool hovered;
    private readonly HashSet<Guid> received = [];
    private DateTimeOffset? lastSound;

    public void Publish(NotificationEvent notification, bool sound)
    {
        if (notification.Subjects.Count == 0 || !received.Add(notification.Id)) return;
        SoundOnArrival(sound);
        if (hovered)
        {
            waiting.Add(notification);
            if (waiting.Count > 2) waiting.RemoveAt(0);
            return;
        }
        Show(notification);
    }
    public void ReceiveSuppressed(NotificationEvent notification, bool sound)
    {
        if (notification.Subjects.Count != 0 && received.Add(notification.Id)) SoundOnArrival(sound);
    }

    public void SetHovered(bool value)
    {
        hovered = value;
        if (value || waiting.Count == 0) return;
        visible.AddRange(waiting);
        waiting.Clear();
        if (visible.Count > 2) visible.RemoveRange(0, visible.Count - 2);
        output.Render(visible.ToArray());
    }

    private void Show(NotificationEvent notification)
    {
        visible.Add(notification);
        if (visible.Count > 2) visible.RemoveAt(0);
        output.Render(visible.ToArray());
    }

    private void SoundOnArrival(bool sound)
    {
        var now = time.GetUtcNow();
        if (sound && (lastSound is null || now - lastSound >= TimeSpan.FromSeconds(1)))
        { lastSound = now; output.PlaySound(); }
    }

    public void Hide(Guid id)
    {
        waiting.RemoveAll(x => x.Id == id);
        if (visible.RemoveAll(x => x.Id == id) > 0) output.Render(visible.ToArray());
    }

    public void HideAll() { visible.Clear(); waiting.Clear(); output.Render([]); }

    public void RemoveSubject(EntityKey key, bool personalOnly, NotificationKind? onlyKind = null)
    {
        for (var index = waiting.Count - 1; index >= 0; index--)
        {
            var item = waiting[index];
            var updated = WithoutSubject(item, key, personalOnly, onlyKind);
            if (updated is null) waiting.RemoveAt(index);
            else waiting[index] = updated;
        }
        var changed = false;
        for (var index = visible.Count - 1; index >= 0; index--)
        {
            var item = visible[index];
            var updated = WithoutSubject(item, key, personalOnly, onlyKind);
            if (ReferenceEquals(updated, item)) continue;
            changed = true;
            if (updated is null) visible.RemoveAt(index);
            else visible[index] = updated;
        }
        if (changed) output.Render(visible.ToArray());
    }

    private static NotificationEvent? WithoutSubject(NotificationEvent item, EntityKey key, bool personalOnly, NotificationKind? onlyKind)
    {
        if (onlyKind != null && item.Kind != onlyKind && item.Kind != NotificationKind.Missed) return item;
        if (personalOnly && item.Kind is not (NotificationKind.Personal or NotificationKind.Summary or NotificationKind.Missed)) return item;
        var remaining = item.Subjects.Where(x => x.Key != key || (personalOnly && x.Origin == NotificationKind.CheckIn) ||
            (onlyKind != null && item.Kind == NotificationKind.Missed && x.Origin != onlyKind)).ToArray();
        return remaining.Length == 0 ? null : remaining.Length == item.Subjects.Count ? item : item with { Subjects = remaining };
    }
}
