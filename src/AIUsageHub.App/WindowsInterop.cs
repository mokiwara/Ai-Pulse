using System.Runtime.InteropServices;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace AIUsageHub;

internal static class WindowsInterop
{
    public const int GWL_EXSTYLE = -20;
    public const int GWL_STYLE = -16;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_CAPTION = 0x00C00000;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newLong);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int length);
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(Point pt, uint flags);
    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }

    public static WinForms.Screen SelectedScreen(string id)
        => id == "primary" ? WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0]
            : WinForms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == id) ?? WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0];

    public static double ScaleFor(WinForms.Screen screen)
    {
        var center = new Point { X = screen.Bounds.Left + screen.Bounds.Width / 2, Y = screen.Bounds.Top + screen.Bounds.Height / 2 };
        try
        {
            var monitor = MonitorFromPoint(center, 2);
            if (GetDpiForMonitor(monitor, 0, out var x, out _) == 0) return x / 96d;
        }
        catch { }
        return 1;
    }

    public static bool HasFullscreenForeground(WinForms.Screen screen, IntPtr ownHwnd)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == ownHwnd || !IsWindowVisible(fg)) return false;
        var name = new System.Text.StringBuilder(64);
        GetClassName(fg, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        var style = GetWindowLongPtr(fg, GWL_STYLE).ToInt64();
        if ((style & WS_CAPTION) != 0) return false;
        if (!GetWindowRect(fg, out var rect)) return false;
        var b = screen.Bounds;
        const int tolerance = 3;
        return rect.Left <= b.Left + tolerance && rect.Top <= b.Top + tolerance &&
               rect.Right >= b.Right - tolerance && rect.Bottom >= b.Bottom - tolerance;
    }
}
