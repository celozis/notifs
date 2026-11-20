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
    private readonly List<(NotificationEvent Event, bool Sound)> waiting = [];
    private bool hovered;
    private readonly HashSet<Guid> received = [];
    private DateTimeOffset? lastSound;

    public void Publish(NotificationEvent notification, bool sound)
    {
        if (notification.Subjects.Count == 0 || !received.Add(notification.Id)) return;
        if (hovered)
        {
            waiting.Add((notification, sound));
            if (waiting.Count > 2) waiting.RemoveAt(0);
            return;
        }
        Show(notification, sound);
    }

    public void SetHovered(bool value)
    {
        hovered = value;
        if (value) return;
        var pending = waiting.ToArray();
        waiting.Clear();
        foreach (var item in pending) Show(item.Event, item.Sound);
    }

    private void Show(NotificationEvent notification, bool sound)
    {
        visible.Add(notification);
        if (visible.Count > 2) visible.RemoveAt(0);
        output.Render(visible.ToArray());
        var now = time.GetUtcNow();
        if (sound && (lastSound is null || now - lastSound >= TimeSpan.FromSeconds(1)))
        { lastSound = now; output.PlaySound(); }
    }

    public void Hide(Guid id)
    {
        waiting.RemoveAll(x => x.Event.Id == id);
        if (visible.RemoveAll(x => x.Id == id) > 0) output.Render(visible.ToArray());
    }

    public void HideAll() { visible.Clear(); waiting.Clear(); output.Render([]); }

    public void RemoveSubject(EntityKey key, bool personalOnly)
    {
        for (var index = waiting.Count - 1; index >= 0; index--)
        {
            var item = waiting[index];
            if (personalOnly && item.Event.Kind is not (NotificationKind.Personal or NotificationKind.Summary)) continue;
            var remaining = item.Event.Subjects.Where(x => x.Key != key).ToArray();
            if (remaining.Length == 0) waiting.RemoveAt(index);
            else waiting[index] = (item.Event with { Subjects = remaining }, item.Sound);
        }
        var changed = false;
        for (var index = visible.Count - 1; index >= 0; index--)
        {
            var item = visible[index];
            if (personalOnly && item.Kind is not (NotificationKind.Personal or NotificationKind.Summary)) continue;
            var remaining = item.Subjects.Where(x => x.Key != key).ToArray();
            if (remaining.Length == item.Subjects.Count) continue;
            changed = true;
            if (remaining.Length == 0) visible.RemoveAt(index);
            else visible[index] = item with { Subjects = remaining };
        }
        if (changed) output.Render(visible.ToArray());
    }
}
