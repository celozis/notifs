using Microsoft.Win32;
using Notifs.App;

namespace Notifs.Scenarios;

internal static class AutoStartScenarios
{
    public static void Run()
    {
        var key = "Software\\Notifs.Tests\\" + Guid.NewGuid().ToString("N");
        const string executable = "C:\\Программы пользователя\\Notifs\\Notifs.App.exe";
        try
        {
            var startup = new WindowsAutoStart(executable, key);
            Check(!startup.IsEnabled, "clean user startup registration is disabled");
            startup.SetEnabled(true);
            Check(new WindowsAutoStart(executable, key).IsEnabled, "startup registration survives reopening");
            using (var saved = Registry.CurrentUser.OpenSubKey(key))
                Check((string?)saved!.GetValue("Notifs") == "\"C:\\Программы пользователя\\Notifs\\Notifs.App.exe\" --background", "Windows receives quoted Unicode executable and background argument, without SDK commands");
            using (var saved = Registry.CurrentUser.CreateSubKey(key)) saved.SetValue("OtherApp", "untouched");
            startup.SetEnabled(false);
            Check(!new WindowsAutoStart(executable, key).IsEnabled, "disable removes the startup entry");
            using (var saved = Registry.CurrentUser.OpenSubKey(key))
                Check(saved!.GetValue("Notifs") is null && (string?)saved.GetValue("OtherApp") == "untouched", "disable preserves unrelated startup registrations");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false); }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
}
