using System.Runtime.InteropServices;

namespace AIUsageHub;

// Recognize two complete, unmodified taps; never consume keyboard input.
public sealed class DoubleAltGesture
{
    private readonly HashSet<int> _down = [];
    private long? _pressedAt, _lastTap;
    private bool _chord;

    public bool Process(int key, bool down, long milliseconds)
    {
        var alt = key is 0x12 or 0xA4 or 0xA5;
        if (down)
        {
            if (!_down.Add(key)) return false;
            if (!alt) { _chord = true; _lastTap = null; return false; }
            _pressedAt = milliseconds;
            _chord = _down.Count != 1;
            return false;
        }
        _down.Remove(key);
        if (!alt) return false;
        var valid = !_chord && _pressedAt is { } start && milliseconds - start <= 250;
        _pressedAt = null;
        if (!valid) { _lastTap = null; return false; }
        if (_lastTap is { } last && milliseconds - last <= 400)
        { _lastTap = null; return true; }
        _lastTap = milliseconds;
        return false;
    }
}

public sealed class DoubleAltShortcut : IDisposable
{
    private readonly HookProc _callback;
    private readonly DoubleAltGesture _gesture = new();
    private IntPtr _hook;

    public DoubleAltShortcut(Action toggle)
    {
        _callback = (code, message, data) =>
        {
            try
            {
            if (code >= 0 && message.ToInt64() is 0x100 or 0x101 or 0x104 or 0x105)
            {
                var key = Marshal.ReadInt32(data);
                var flags = Marshal.ReadInt32(data, 8);
                if ((flags & 0x10) == 0 && _gesture.Process(key, message.ToInt64() is 0x100 or 0x104, Environment.TickCount64)) toggle();
            }
            }
            catch (InvalidOperationException) { /* Dispatcher may be shutting down; never interrupt keyboard delivery. */ }
            return CallNextHookEx(_hook, code, message, data);
        };
        _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        GC.KeepAlive(_callback);
    }
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
}
