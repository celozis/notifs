using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static class BackgroundProcessScenarios
{
    public static void Run(string directory)
    {
        var children = new List<Process>();
        Process Launch(string arguments = "")
        {
            var child = StartApp(directory, arguments);
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
    internal static async Task CheckForegroundAsync(Window typing, string directory)
    {
        using var owner = StartApp(directory, "--background");
        try
        {
            var service = new TaskService(Path.Combine(directory, "notifs.db"));
            var timeout = Stopwatch.StartNew();
            while (!CanRead(service)) { if (timeout.ElapsedMilliseconds > 15000) throw new TimeoutException("background owner not ready"); await Task.Delay(25); }
            typing.Activate();
            var typingHandle = new System.Windows.Interop.WindowInteropHelper(typing).Handle;
            // Test setup: give the launcher its own foreground window so Windows can grant
            // the newly started secondary the same rights as a user launching from Explorer.
            var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            var testThread = GetCurrentThreadId();
            if (foregroundThread != testThread) AttachThreadInput(testThread, foregroundThread, true);
            try { SetForegroundWindow(typingHandle); }
            finally { if (foregroundThread != testThread) AttachThreadInput(testThread, foregroundThread, false); }
            if (GetForegroundWindow() != typingHandle) throw new Exception("foreground test requires its independent typing window to have focus");
            using var secondary = StartApp(directory);
            timeout.Restart();
            while (!secondary.HasExited || FindVisible(owner.Id, "Notifs — задачи") == 0 || FindVisible(owner.Id, "Notifs — задачи") != GetForegroundWindow())
            { if (timeout.ElapsedMilliseconds > 15000) throw new TimeoutException("secondary did not transfer foreground to the owner"); await Task.Delay(25); }
            Console.WriteLine("PASS real secondary launch transfers foreground from the independent typing window to the original process");
        }
        finally { if (!owner.HasExited) { owner.Kill(entireProcessTree: true); owner.WaitForExit(); } }
    }
    private static Process StartApp(string directory, string arguments = "")
    {
        var executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Notifs.App/bin/Release/net10.0-windows/Notifs.App.exe"));
        var start = new ProcessStartInfo(executable, arguments) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        start.Environment["NOTIFS_DATA_DIR"] = directory;
        // Works when the scenario itself is launched by dotnet.exe or by its own apphost.
        var runtimeRoot = Directory.GetParent(RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar))?.Parent?.Parent?.FullName;
        if (runtimeRoot is not null && File.Exists(Path.Combine(runtimeRoot, "dotnet.exe")))
        { start.Environment["DOTNET_ROOT"] = runtimeRoot; start.Environment["DOTNET_ROOT_X64"] = runtimeRoot; }
        return Process.Start(start)!;
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
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
