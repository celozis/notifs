using System.Runtime.InteropServices;
using System.Text;
using Notifs.Core;
using Forms = System.Windows.Forms;

namespace Notifs.App;

public static class WindowsNotificationMonitors
{
    public static IReadOnlyList<NotificationMonitor> List() => Forms.Screen.AllScreens.Select(screen =>
    {
        var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        var found = EnumDisplayDevices(screen.DeviceName, 0, ref device, 1);
        var id = found && !string.IsNullOrWhiteSpace(device.Id) ? device.Id : screen.DeviceName;
        var name = (found ? device.Description : screen.DeviceName) + " · " + screen.DeviceName + (screen.Primary ? " · основной" : "");
        var bounds = screen.Bounds; var work = screen.WorkingArea;
        return new NotificationMonitor(id, name, screen.Primary, new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), new(work.Left, work.Top, work.Right, work.Bottom));
    }).ToArray();

    public static bool ForegroundFullscreenOnPrimary()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0 || IsIconic(hwnd) || !IsWindowVisible(hwnd)) return false;
        var type = new StringBuilder(256); GetClassName(hwnd, type, type.Capacity);
        if (type.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        var screen = Forms.Screen.FromHandle(hwnd); if (!screen.Primary) return false;
        var style = GetWindowLongPtr(hwnd, -16).ToInt64();
        // Captioned maximized windows can cover the desktop with hidden taskbars; they are still ordinary windows.
        if ((style & 0x00C00000) != 0) return false;
        if (!GetWindowRect(hwnd, out var rect)) return false;
        var bounds = screen.Bounds;
        var covers = rect.Left <= bounds.Left + 1 && rect.Top <= bounds.Top + 1 && rect.Right >= bounds.Right - 1 && rect.Bottom >= bounds.Bottom - 1;
        if (covers) return true;
        return SHQueryUserNotificationState(out var state) == 0 && state == 3;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
}
