using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using Notifs.Core;

namespace Notifs.App;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed record TaskRow(TaskItem Item)
{
    public string Title => Item.Title;
    public string Description => Item.Description;
    public string CreatedLabel => "Создана " + Item.CreatedAt.ToLocalTime().ToString("d MMMM yyyy, HH:mm");
    public string CompletedLabel => Item.CompletedAt is { } value ? "Выполнена " + value.ToLocalTime().ToString("d MMMM yyyy, HH:mm") : "В работе";
}

public sealed class MainViewModel : Observable
{
    private readonly TaskService service;
    private TaskRow? selected;
    private bool history;
    private bool busy;
    private bool initialized;
    private string status = "Открываем ваши задачи…";
    private string error = "";
    public ObservableCollection<TaskRow> Tasks { get; } = [];
    public TaskRow? Selected { get => selected; set { selected = value; Changed(); Changed(nameof(HasSelection)); CommandManager.InvalidateRequerySuggested(); } }
    public bool HasSelection => Selected is not null;
    public bool IsHistory => history;
    public bool IsActive => !history;
    public string ActiveNavigationStatus => history ? "" : "Текущий раздел";
    public string HistoryNavigationStatus => history ? "Текущий раздел" : "";
    public bool IsBusy { get => busy; private set { busy = value; Changed(); CommandManager.InvalidateRequerySuggested(); } }
    public string PageTitle => history ? "История" : "Мои задачи";
    public string Subtitle => history ? "Всё, что уже получилось сделать." : "Освободите голову. Начните с одного дела.";
    public string CountLabel => history ? $"Выполнено: {Tasks.Count}" : $"В работе: {Tasks.Count}";
    public string EmptyTitle => history ? "Здесь будут ваши результаты" : "Место для ваших планов";
    public string EmptyMessage => history ? "Выполненные задачи сохраняются здесь. Любую можно вернуть в работу." : "Добавьте первое дело — короткую мысль или задачу с подробным описанием.";
    public bool IsEmpty => Tasks.Count == 0;
    public string Status { get => status; private set { status = value; Changed(); } }
    public string Error { get => error; private set { error = value; Changed(); Changed(nameof(HasError)); } }
    public bool HasError => Error.Length > 0;
    public AsyncCommand ActiveCommand { get; }
    public AsyncCommand HistoryCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand CompleteCommand { get; }
    public AsyncCommand RestoreCommand { get; }

    public MainViewModel(TaskService service)
    {
        this.service = service;
        ActiveCommand = new(() => SwitchAsync(false), () => !IsBusy);
        HistoryCommand = new(() => SwitchAsync(true), () => !IsBusy);
        RefreshCommand = new(() => LoadAsync(), () => !IsBusy);
        CompleteCommand = new(() => ChangeStateAsync(false), () => HasSelection && !IsBusy && !HasError && !IsHistory);
        RestoreCommand = new(() => ChangeStateAsync(true), () => HasSelection && !IsBusy && !HasError && IsHistory);
    }

    public async Task LoadAsync(Guid? selectId = null)
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = "";
        try
        {
            var rows = await Task.Run(() =>
            {
                if (!initialized) { service.Initialize(); initialized = true; }
                return service.List(history);
            });
            var id = selectId ?? Selected?.Item.Id;
            Tasks.Clear();
            foreach (var item in rows) Tasks.Add(new(item));
            Selected = Tasks.FirstOrDefault(row => row.Item.Id == id) ?? Tasks.FirstOrDefault();
            Status = "Все изменения сохранены на этом компьютере";
            RefreshLabels();
        }
        catch (Exception ex) when (IsStorageError(ex)) { ShowStorageError(); }
        finally { IsBusy = false; }
    }

    private async Task SwitchAsync(bool showHistory)
    {
        history = showHistory;
        Selected = null;
        Tasks.Clear();
        RefreshLabels();
        await LoadAsync();
    }

    private async Task ChangeStateAsync(bool restore)
    {
        if (Selected is not { } row) return;
        IsBusy = true;
        Error = "";
        try
        {
            await Task.Run(() => { if (restore) service.Restore(row.Item.Id); else service.Complete(row.Item.Id); });
            IsBusy = false;
            await LoadAsync();
            Status = restore ? "Задача возвращена в работу" : "Задача выполнена. Она доступна в истории";
        }
        catch (Exception ex) when (IsStorageError(ex)) { ShowStorageError(); }
        finally { IsBusy = false; }
    }

    public EditorViewModel CreateEditor(bool edit) => new(service, edit ? Selected?.Item : null);

    public async Task EditorSavedAsync(Guid id, bool created)
    {
        // A newly created task always opens in the active list.
        if (created) history = false;
        await LoadAsync(id);
    }

    private void RefreshLabels()
    {
        foreach (var property in new[] { nameof(IsHistory), nameof(IsActive), nameof(ActiveNavigationStatus), nameof(HistoryNavigationStatus), nameof(PageTitle), nameof(Subtitle), nameof(CountLabel), nameof(EmptyTitle), nameof(EmptyMessage), nameof(IsEmpty) }) Changed(property);
    }
    private void ShowStorageError() { Error = "Не удалось открыть или сохранить задачи. Проверьте доступ к папке данных и нажмите «Повторить»."; Status = "Изменения не подтверждены"; }
    internal static bool IsStorageError(Exception ex) => ex is SqliteException or IOException or UnauthorizedAccessException or KeyNotFoundException;
}

public sealed class EditorViewModel : Observable
{
    private readonly TaskService service;
    private readonly TaskItem? original;
    private string title;
    private string description;
    private string error = "";
    private bool busy;
    public event Action<Guid>? Saved;
    public event Action? ValidationFailed;
    public string Heading => original is null ? "Новое дело" : "Редактировать задачу";
    public string Title { get => title; set { title = value; Changed(); Changed(nameof(IsDirty)); } }
    public string Description { get => description; set { description = value; Changed(); Changed(nameof(IsDirty)); } }
    public string Error { get => error; private set { error = value; Changed(); Changed(nameof(HasError)); } }
    public bool HasError => Error.Length > 0;
    public bool IsBusy { get => busy; private set { busy = value; Changed(); Changed(nameof(CanEdit)); Changed(nameof(SaveLabel)); CommandManager.InvalidateRequerySuggested(); } }
    public bool CanEdit => !IsBusy;
    public string SaveLabel => IsBusy ? "Сохраняем…" : "Сохранить задачу";
    public bool IsDirty => Title != (original?.Title ?? "") || Description != (original?.Description ?? "");
    public AsyncCommand SaveCommand { get; }
    public EditorViewModel(TaskService service, TaskItem? original)
    {
        this.service = service;
        this.original = original;
        title = original?.Title ?? "";
        description = original?.Description ?? "";
        SaveCommand = new(SaveAsync, () => !IsBusy);
    }
    private async Task SaveAsync()
    {
        Error = "";
        if (string.IsNullOrWhiteSpace(Title)) { Error = "Введите название задачи. Описание останется на месте."; ValidationFailed?.Invoke(); return; }
        IsBusy = true;
        try
        {
            var id = await Task.Run(() =>
            {
                if (original is null) return service.Create(Title, Description).Id;
                service.Edit(original.Id, Title, Description);
                return original.Id;
            });
            Saved?.Invoke(id);
        }
        catch (Exception ex) when (MainViewModel.IsStorageError(ex)) { Error = "Не удалось сохранить. Проверьте доступ к папке данных и попробуйте ещё раз. Ваш текст остаётся в форме."; }
        finally { IsBusy = false; }
    }
}
