using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static class PortableScenarios
{
    public static void Run(string package)
    {
        package = Path.GetFullPath(package);
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(package, "release-manifest.json"))))
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(package, file.GetProperty("path").GetString()!));
            if (!path.StartsWith(package + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Manifest path escapes package");
            var actual = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            Check(actual == file.GetProperty("sha256").GetString(), "package file hash: " + Path.GetFileName(path));
        }
        var root = Path.GetFullPath(Path.Combine(".artifacts", "portable-smoke", Guid.NewGuid().ToString("N")));
        var first = Path.Combine(root, "версия 1 с пробелами"); var second = Path.Combine(root, "обновлённая версия 2");
        Copy(package, first); Copy(package, second);
        var profile = Path.Combine(root, "отдельный профиль данных");
        var service = new TaskService(Path.Combine(profile, "notifs.db")); service.Initialize();
        var task = service.Create("Автономный запуск", "# Markdown\n\nБез **сети** и установленного runtime", estimateMinutes: 40);
        var draft = new TaskDraft("Сохранённый черновик", "*выделение*", "невалидная дата", "", "", "", ImportanceRank.S);
        service.SaveDraft(null, draft);
        var deleted = service.Create("Последнее удаление", ""); service.DeleteTask(deleted.Id);
        var calendar = new CalendarState(new(service.Today.Year, service.Today.Month, 1), service.Today, RangeStart: service.Today, RangeEnd: service.Today.AddDays(21));
        service.SaveCalendarState(calendar);
        var settings = new NotificationSettings(false, 45); service.SaveNotificationSettings(settings);
        var filters = new TaskListSettings(Sort: TaskSort.Rank) { Ranks = [ImportanceRank.S, ImportanceRank.A] };
        service.SaveTaskListSettings(TaskSection.InProgress, filters);
        using (var process = Launch(first, profile))
        {
            try
            {
                Wait(() => { process.Refresh(); return process.MainWindowHandle != 0; }, process);
                var runtime = process.Modules.Cast<ProcessModule>().Single(x => string.Equals(x.ModuleName, "coreclr.dll", StringComparison.OrdinalIgnoreCase));
                Check(runtime.FileName.StartsWith(first + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "EXE loads its own runtime with SDK paths and runtime roots unavailable");
                service.StartSession(task.Id);
                Wait(() => { var session = service.ReadSession(task.Id)!; if (session.RunningSince == null) service.ResumeSession(task.Id); return session.ActiveTicks > 0; }, process);
            }
            finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
        }
        var checkpoint = service.ReadSession(task.Id)!.ActiveTicks;
        using (var process = Launch(second, profile))
        {
            try
            {
                Wait(() => { process.Refresh(); return process.MainWindowHandle != 0; }, process);
                Wait(() => service.ReadSession(task.Id)!.RunningSince == null, process);
                Check(service.ListAll().Single() == task && service.ReadDraft(null) == draft && service.ReadCalendarState() == calendar && service.ReadNotificationSettings() == settings,
                    "moving portable EXE to another Cyrillic/spaced directory preserves tasks, raw draft, calendar and settings");
                Check(service.ReadTaskListSettings(TaskSection.InProgress).Ranks.SequenceEqual(filters.Ranks), "independent section filters survive folder replacement");
                Check(service.ReadSession(task.Id) is { RunningSince: null } session && session.ActiveTicks == checkpoint, "real packaged process crash recovers paused at its last durable checkpoint");
                Check(service.RestoreDeleted().Id == deleted.Id, "deletion recovery slot survives packaged crash and folder update");
            }
            finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
        }
        Console.WriteLine("PORTABLE PASS " + root);
    }
    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
    }
    private static Process Launch(string folder, string profile)
    {
        var info = new ProcessStartInfo(Path.Combine(folder, "Notifs.App.exe")) { UseShellExecute = false, WorkingDirectory = folder, WindowStyle = ProcessWindowStyle.Hidden };
        info.Environment["NOTIFS_DATA_DIR"] = profile;
        info.Environment["DOTNET_ROOT"] = Path.Combine(folder, "no-installed-runtime"); info.Environment["DOTNET_ROOT_X64"] = info.Environment["DOTNET_ROOT"];
        info.Environment["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System);
        info.Environment["HTTP_PROXY"] = "http://127.0.0.1:1"; info.Environment["HTTPS_PROXY"] = "http://127.0.0.1:1";
        return Process.Start(info)!;
    }
    private static void Wait(Func<bool> ready, Process process)
    {
        var elapsed = Stopwatch.StartNew();
        while (!ready()) { if (process.HasExited || elapsed.Elapsed > TimeSpan.FromSeconds(20)) throw new IOException("Portable application failed to start or checkpoint"); Thread.Sleep(50); }
    }
    private static void Check(bool value, string message) => DraftScenarios.Check(value, message);
}
