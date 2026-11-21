using Notifs.Core;

namespace Notifs.App;

public sealed partial class MainViewModel
{
    private bool inProgress;
    public bool IsInProgress => inProgress;
    public int InProgressCount { get; private set; }
    public string DateLabel => service.LocalNow.ToString("dddd, d MMMM").ToUpperInvariant();
    public string ClockLabel => service.LocalNow.ToString("HH:mm");
    public AsyncCommand StartSessionCommand { get; private set; } = null!;
    public AsyncCommand PauseSessionCommand { get; private set; } = null!;
    public AsyncCommand ResumeSessionCommand { get; private set; } = null!;
    public AsyncCommand CancelSessionCommand { get; private set; } = null!;
    private void InitializeSessions()
    {
        StartSessionCommand = new(() => ChangeSessionAsync(() => service.StartSession(Selected!.Item.Id)), () => !IsBusy && Selected is { CanStartSession: true });
        PauseSessionCommand = new(() => ChangeSessionAsync(() => service.PauseSession(Selected!.Item.Id)), () => !IsBusy && Selected is { SessionRunning: true });
        ResumeSessionCommand = new(() => ChangeSessionAsync(() => service.ResumeSession(Selected!.Item.Id)), () => !IsBusy && Selected is { SessionPaused: true });
        CancelSessionCommand = new(() => ChangeSessionAsync(() => service.CancelSession(Selected!.Item.Id)), () => !IsBusy && Selected is { HasSession: true });
    }
    private async Task ChangeSessionAsync(Action action)
    {
        var selection = Selected?.Item.Id;
        IsBusy = true; Error = "";
        try { await Task.Run(action); }
        catch (Exception ex) when (IsStorageError(ex)) { ShowStorageError(); }
        finally { IsBusy = false; }
        if (!HasError) { await LoadAsync(selection); Status = "Состояние занятия сохранено"; }
    }
    public Task RefreshSourcesAsync() => LoadAsync();
    public async Task OpenInProgressAsync()
    {
        inProgress = true; calendar = false; history = false;
        await LoadAsync(reapplyFilter: true);
    }
}

public sealed partial class TaskRow
{
    private TaskSession? session;
    private TaskSessionTiming? sessionTiming;
    public bool HasSession => session is { Finished: false } && IsIncomplete;
    public bool SessionRunning => HasSession && session!.RunningSince != null;
    public bool SessionPaused => HasSession && !SessionRunning;
    public bool CanStartSession => IsIncomplete && !HasSession;
    public string SessionStateLabel => SessionRunning ? "В ПРОЦЕССЕ" : SessionPaused ? "НА ПАУЗЕ" : "ПОДРОБНОСТИ ЗАДАЧИ";
    public string SessionLabel => HasSession ? (SessionRunning ? "▷ В процессе" : "Ⅱ На паузе") + " · с " + service.LocalTime(session!.StartedAt).ToString("HH:mm") : "";
    public string SessionStartedLabel => session == null ? "" : "Начал " + service.LocalTime(session.StartedAt).ToString("d MMMM, HH:mm");
    public string SessionActiveLabel => sessionTiming == null ? "" : "Активное время: " + Duration(sessionTiming.Active) + ", без пауз";
    public string SessionDetailLabel => session?.TargetActiveTicks == null ? "Без оценки — вопрос не назначен" : session.QuestionAsked ? "Оценка прошла · вопрос уже показан" :
        (SessionPaused ? "На паузе · осталось " : "До вопроса: ") + Duration(sessionTiming?.Remaining ?? TimeSpan.Zero);
    public string SessionQuestionLabel => !HasSession ? "" : SessionPaused ? "Отсчёт заморожен" : sessionTiming?.QuestionAt is { } at ? "Вопрос о результате в " + service.LocalTime(at).ToString("d MMMM, HH:mm") : "";
    public void RefreshSession(TaskSession? value)
    {
        session = value;
        sessionTiming = value == null ? null : service.SessionTiming(value);
        foreach (var name in new[] { nameof(HasSession), nameof(SessionRunning), nameof(SessionPaused), nameof(CanStartSession), nameof(SessionStateLabel), nameof(SessionLabel), nameof(SessionStartedLabel), nameof(SessionActiveLabel), nameof(SessionDetailLabel), nameof(SessionQuestionLabel) }) Changed(name);
    }
}
