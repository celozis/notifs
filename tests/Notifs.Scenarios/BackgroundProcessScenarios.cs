using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static class BackgroundProcessScenarios
{
    public static void Run(string directory)
    {
        var executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Notifs.App/bin/Release/net10.0-windows/Notifs.App.exe"));
        var children = new List<Process>();
        Process Launch(string arguments = "")
        {
            var start = new ProcessStartInfo(executable, arguments) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
            start.Environment["NOTIFS_DATA_DIR"] = directory;
            start.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(Environment.ProcessPath)!;
            var child = Process.Start(start)!;
            children.Add(child);
            return child;
        }
        try
        {
            for (var i = 0; i < 4; i++) Launch();
            Wait(() => children.Count(x => !x.HasExited) == 1 && children.Single(x => !x.HasExited).MainWindowHandle != 0,
                "rapid launches must leave exactly one process and window");
            var owner = children.Single(x => !x.HasExited);
            owner.Refresh();
            var window = owner.MainWindowHandle;
            PostMessage(window, 0x0010, 0, 0); // Real WM_CLOSE follows the title-bar close path.
            Wait(() => !IsWindowVisible(window), "close must hide the main window");
            if (owner.HasExited) throw new Exception("close terminated the scheduler process");
            Console.WriteLine("PASS title-bar close keeps the real application process alive");
            var second = Launch();
            Wait(() => second.HasExited && IsWindowVisible(window), "second launch must restore the original window");
            if (owner.HasExited) throw new Exception("original process replaced during activation");
            Console.WriteLine("PASS simultaneous launches and reactivation use one process and the original HWND");
            foreach (var child in children.Where(x => !x.HasExited)) { child.Kill(entireProcessTree: true); child.WaitForExit(); }
            directory = Path.Combine(directory, "fresh-background-profile");
            var service = new TaskService(Path.Combine(directory, "notifs.db"));
            var background = Launch("--background");
            Wait(() => CanRead(service), "first background launch must initialize a fresh database");
            var now = DateTime.Now.AddMinutes(-1);
            service.SaveEditor(null, "Напоминание при фоновом запуске", "", null, null, ImportanceRank.B, new(DateOnly.FromDateTime(now), TimeOnly.FromDateTime(now)));
            Wait(() => FindVisible(background.Id, "Notifs — уведомления") != 0, "background launch must initialize storage and deliver a real notification without showing the main window");
            if (FindVisible(background.Id, "Notifs — задачи") != 0) throw new Exception("background launch showed the main window");
            Console.WriteLine("PASS hidden startup initializes SQLite and delivers personal reminders without showing the main window");
        }
        finally
        {
            foreach (var child in children)
            {
                if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(); }
                child.Dispose();
            }
        }
    }

    private static void Wait(Func<bool> condition, string message)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(15)) throw new Exception(message);
            Thread.Sleep(50);
        }
    }
    private static bool CanRead(TaskService service)
    {
        try { service.ListAll(); return true; }
        catch (Microsoft.Data.Sqlite.SqliteException) { return false; }
    }
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    private static nint FindVisible(int processId, string title)
    {
        nint found = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            var text = new StringBuilder(256); GetWindowText(window, text, text.Capacity);
            if (owner == processId && IsWindowVisible(window) && text.ToString() == title) found = window;
            return true;
        }, 0);
        return found;
    }
    private delegate bool EnumWindow(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out int processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder title, int count);
}
