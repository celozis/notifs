using System.IO;
using Microsoft.Win32;

namespace Notifs.App;

/// <summary>The current user's Run entry is the source of truth; no independent saved flag.</summary>
public class WindowsAutoStart
{
    private readonly string key;
    public string LaunchCommand { get; }
    public WindowsAutoStart(string? executablePath = null, string key = @"Software\Microsoft\Windows\CurrentVersion\Run")
    {
        this.key = key;
        var executable = Path.GetFullPath(executablePath ?? Path.Combine(AppContext.BaseDirectory, "Notifs.App.exe"));
        LaunchCommand = $"\"{executable}\" --background";
    }
    public bool IsEnabled => string.Equals(ReadCommand(), LaunchCommand, StringComparison.OrdinalIgnoreCase);
    public virtual string? ReadCommand()
    {
        using var run = Registry.CurrentUser.OpenSubKey(key);
        return run?.GetValue("Notifs") as string;
    }
    public virtual void WriteCommand(string? command)
    {
        using var run = Registry.CurrentUser.CreateSubKey(key, writable: true);
        if (command is null) run.DeleteValue("Notifs", throwOnMissingValue: false);
        else run.SetValue("Notifs", command, RegistryValueKind.String);
    }
    public void SetEnabled(bool enabled) => WriteCommand(enabled ? LaunchCommand : null);
}
