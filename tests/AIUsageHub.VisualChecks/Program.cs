using AIUsageHub;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class Program
{
    private static string Output = "";
    [STAThread] public static void Main(string[] args)
    {
        Output = Path.GetFullPath(args[0]); Directory.CreateDirectory(Output);
        File.Delete(Path.Combine(Output, "error.txt"));
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try { if (args.Contains("--stress")) await Stress(); else await Run(args.Contains("--live")); }
            catch (Exception e) { File.WriteAllText(Path.Combine(Output, "error.txt"), e.ToString()); Environment.ExitCode = 1; }
            app.Shutdown();
        };
        app.Run();
    }
    private static async Task Stress()
    {
        var root = Path.Combine(Path.GetTempPath(), "AIPulseStress", Guid.NewGuid().ToString("N"));
        using var service = new HubService(new LocalStore(root));
        service.Settings.AutomaticRefresh = false;
        service.Contexts.Add(Fixture("Codex", "Stress fixture", 70, 50));
        var references = new List<WeakReference>();
        var process = System.Diagnostics.Process.GetCurrentProcess();
        async Task Cycle(int index)
        {
            var main = new MainWindow(service, () => { }, () => { }) { AllowClose = true };
            main.Show(); main.Open("Settings"); main.Open("Accounts"); main.Open();
            var notch = new NotchWindow(service, () => { }, () => { });
            notch.SetNotchVisible(true); notch.ToggleExpanded(); notch.ToggleExpanded();
            using (var shortcut = new DoubleAltShortcut(() => { })) { }
            await Task.Delay(20);
            main.Close(); notch.Close();
            references.Add(new WeakReference(main)); references.Add(new WeakReference(notch));
        }
        for (var i = 0; i < 5; i++) await Cycle(i);
        await Task.Delay(250);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); process.Refresh();
        var beforeHandles = process.HandleCount; var beforeManaged = GC.GetTotalMemory(true);
        for (var i = 0; i < 60; i++) await Cycle(i);
        await Task.Delay(500);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); process.Refresh();
        var afterHandles = process.HandleCount; var afterManaged = GC.GetTotalMemory(true);
        var alive = references.Count(r => r.IsAlive);
        var report = $"cycles=65; windowsCollected={references.Count - alive}/{references.Count}; handlesBefore={beforeHandles}; handlesAfter={afterHandles}; managedBefore={beforeManaged}; managedAfter={afterManaged}";
        File.WriteAllText(Path.Combine(Output, "stress.txt"), report);
        service.Dispose(); await service.Stopped;
        Directory.Delete(root, true);
        if (alive > 2 || afterHandles - beforeHandles > 20 || afterManaged - beforeManaged > 5_000_000)
            throw new InvalidOperationException("Resource regression: " + report);
    }

    private static async Task Run(bool live)
    {
        var root = Path.Combine(Path.GetTempPath(), "HubVisual", Guid.NewGuid().ToString("N"));
        using var service = new HubService(new LocalStore(root));
        service.Settings.AnimateNotch = true;
        service.Settings.AutomaticRefresh = false;
        var now = DateTimeOffset.UtcNow;
        if (live)
        {
            foreach (var c in new LocalStore().LoadContexts())
            {
                if (c.Provider == "Codex")
                {
                    var read = await new CodexConnector().ReadAsync(c, CancellationToken.None);
                    c.Snapshot = read.Snapshot; c.ErrorCode = null;
                    File.AppendAllText(Path.Combine(Output, "live-readings.txt"), c.Name + ": " + string.Join(", ", c.Snapshot.Windows.Select(w => $"{w.Title}: {w.UsedPercent}% used, {w.RemainingPercent}% left, reset {w.ResetsAt:O}")) + "\n");
                }
                else { c.ErrorCode = (await new ClaudeConnector().ProbeAsync(c, CancellationToken.None)).Error; }
                c.LastAttemptAt = now; service.Contexts.Add(c);
            }
        }
        else
        {
            service.Contexts.Add(Fixture("Codex", "Personal", 68, 43));
            service.Contexts.Add(Fixture("Codex", "Work", 82, 71));
            service.Contexts.Add(Fixture("Codex", "Client", 52, 56));
            service.Contexts.Add(Fixture("Claude", "Personal", 58, 37));
            service.Settings.CodexNotchAccountKey = ContextIdentity.AccountKey(service.Contexts[1]);
        }
        service.Settings.Theme = "Dark";
        var main = new MainWindow(service, () => { }, () => { }) { AllowClose = true };
        foreach (var page in new[] { "Overview", "Accounts", "Settings" })
        {
            main.Open(page); await Task.Delay(250); Capture(main, "main-" + page.ToLowerInvariant());
        }
        service.Settings.Theme = "Light"; main.Open("Settings"); await Task.Delay(150); Capture(main,"main-settings-light");
        service.Settings.Theme = "Dark"; main.Close();
        var focus = new System.Windows.Window { Title = "Notch interaction check", Width = 360, Height = 120, Left = 20, Top = 150, Content = "Foreground control window" };
        focus.Show(); focus.Activate();
        var refreshClicks = 0;
        var notch = new NotchWindow(service, () => { }, () => { }, () => { refreshClicks++; return Task.CompletedTask; });
        notch.Show(); await Task.Delay(300);
        var hwnd = new WindowInteropHelper(notch).Handle;
        var before = GetForegroundWindow();
        Capture(notch, live ? "collapsed-live" : "collapsed-normal-fixture");
        var collapsedHeight = notch.Height;
        var refreshButton = FindVisual<System.Windows.Controls.Button>(notch, "NotchRefreshButton")
            ?? throw new InvalidOperationException("Collapsed notch refresh button was not found.");
        var refreshIcon = FindVisual<System.Windows.Shapes.Path>(refreshButton, "NotchRefreshIcon")
            ?? throw new InvalidOperationException("Refresh icon was not found.");
        var iconCenter = refreshIcon.TranslatePoint(new System.Windows.Point(refreshIcon.ActualWidth / 2, refreshIcon.ActualHeight / 2), refreshButton);
        var iconCentered = Math.Abs(iconCenter.X - refreshButton.ActualWidth / 2) < 1 &&
            Math.Abs(iconCenter.Y - refreshButton.ActualHeight / 2) < 1;
        GetWindowRect(hwnd, out var initialBounds);
        var display = System.Windows.Forms.Screen.PrimaryScreen!;
        var notchCentered = Math.Abs((initialBounds.Left + initialBounds.Right) / 2 -
            (display.Bounds.Left + display.Bounds.Right) / 2) <= 2;
        Click(refreshButton, refreshButton.ActualWidth / 2, refreshButton.ActualHeight / 2);
        await Task.Delay(120);
        var refreshStaysCollapsed = Math.Abs(notch.Height - collapsedHeight) < 1 && before == GetForegroundWindow();
        File.AppendAllText(Path.Combine(Output, "interaction.txt"), $"refreshClicks={refreshClicks}; refreshStaysCollapsed={refreshStaysCollapsed}; refreshIconCentered={iconCentered}; notchCentered={notchCentered}\n");
        if (refreshClicks != 1 || !refreshStaysCollapsed || !iconCentered || !notchCentered)
            throw new InvalidOperationException("Notch refresh layout or click behavior failed.");
        if (!live)
        {
            refreshButton = FindVisual<System.Windows.Controls.Button>(notch, "NotchRefreshButton")!;
            var hoverPoint = refreshButton.PointToScreen(new System.Windows.Point(refreshButton.ActualWidth / 2, refreshButton.ActualHeight / 2));
            SetCursorPos((int)hoverPoint.X - 35, (int)hoverPoint.Y); await Task.Delay(40);
            SetCursorPos((int)hoverPoint.X, (int)hoverPoint.Y); await Task.Delay(100);
            Capture(notch, "collapsed-refresh-hover-fixture");
        }
        if (!live)
        {
            var claude = service.Contexts.Last();
            var original = claude.Snapshot;
            claude.Snapshot = original! with { Windows = original.Windows.Where(w => w.Kind == "WEEKLY").ToArray() };
            service.SaveSettings(); await Task.Delay(100);
            Capture(notch, "collapsed-weekly-only-fixture");
            claude.Snapshot = original;
            service.SaveSettings(); await Task.Delay(100);
        }
        // Real mouse down/up, including WM_MOUSEACTIVATE and the global outside-click hook.
        Click(notch, notch.Width / 2, 24);
        await Task.Delay(300);
        var keptFocus = before == GetForegroundWindow();
        Capture(notch, live ? "expanded-live" : "expanded-fixture");
        if (!live)
        {
            foreach (var dpi in new[] { 120, 144, 192 })
            {
                var scaled = new RenderTargetBitmap((int)Math.Ceiling(notch.Width * dpi / 96), (int)Math.Ceiling(notch.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                scaled.Render(notch);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(scaled));
                using var file = File.Create(Path.Combine(Output, $"expanded-fixture-{dpi}dpi.png")); png.Save(file);
            }
        }
        var openHeight = notch.Height;
        SetCursorPos(30, 170); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(150);
        var outsideClosed = notch.Height < openHeight;
        File.AppendAllText(Path.Combine(Output, "interaction.txt"), $"live={live}; noFocusSteal={keptFocus}; outsideClosed={outsideClosed}; collapsedWindow={notch.Width}x{notch.Height}; noActivate={(GetWindowLongPtr(hwnd, -20).ToInt64() & 0x08000000) != 0}\n");
        notch.ToggleExpanded();
        SetForegroundWindow(hwnd); await Task.Delay(150);
        var keyboardFocus = GetForegroundWindow() == hwnd;
        keybd_event(0x1B, 0, 0, UIntPtr.Zero); keybd_event(0x1B, 0, 2, UIntPtr.Zero);
        await Task.Delay(150);
        var escapeClosed = notch.Height < openHeight;
        File.AppendAllText(Path.Combine(Output, "interaction.txt"), $"explicitKeyboardFocus={keyboardFocus}; escapeClosed={escapeClosed}\n");
        if (!escapeClosed) notch.ToggleExpanded();
        focus.Activate();
        if (!live)
        {
            service.Contexts.Last().Snapshot = Fixture("Claude", "Personal", 12, 37).Snapshot;
            service.SaveSettings(); await Task.Delay(100);
            Capture(notch, "collapsed-warning-fixture");
            var claude = service.Contexts.Last(); claude.Snapshot = null; claude.ErrorCode = "CLAUDE_AUTH_REQUIRED";
            service.SaveSettings(); notch.ToggleExpanded(); await Task.Delay(250);
            Capture(notch, "claude-logged-out-fixture");
            claude.ErrorCode = null; service.SaveSettings(); await Task.Delay(100);
            Capture(notch, "claude-waiting-fixture");
            claude.Snapshot = Fixture("Claude", "Personal", 63, 48).Snapshot! with { FetchedAt = now.AddMinutes(-17) };
            service.SaveSettings(); await Task.Delay(100);
            Capture(notch, "claude-stale-fixture");
            notch.ToggleExpanded();
        }
        // Test fullscreen hide and restore against a real borderless foreground window.
        var primary = System.Windows.Forms.Screen.PrimaryScreen!;
        using (var fullscreen = new System.Windows.Forms.Form { FormBorderStyle = System.Windows.Forms.FormBorderStyle.None, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Bounds = primary.Bounds, TopMost = true })
        {
            fullscreen.Show(); fullscreen.Activate(); await Task.Delay(2300);
            var isForeground = GetForegroundWindow() == fullscreen.Handle;
            var hidden = !notch.IsVisible;
            fullscreen.Close(); await Task.Delay(2300);
            File.AppendAllText(Path.Combine(Output, "interaction.txt"), $"fullscreenForeground={isForeground}; fullscreenHidden={hidden}; restored={notch.IsVisible}; monitors={System.Windows.Forms.Screen.AllScreens.Length}\n");
        }
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            service.Settings.DisplayId = screen.DeviceName; notch.RefreshPlacement(); await Task.Delay(150);
            GetWindowRect(hwnd, out var bounds);
            File.AppendAllText(Path.Combine(Output, "interaction.txt"), $"monitor={screen.DeviceName}; dpi={GetDpiForWindow(hwnd)}; rect={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}; centered={Math.Abs((bounds.Left + bounds.Right) / 2 - (screen.Bounds.Left + screen.Bounds.Right) / 2) <= 2}\n");
        }
        notch.SetNotchVisible(false); await Task.Delay(350);
        var toggleHidden = !notch.IsVisible;
        notch.SetNotchVisible(true); await Task.Delay(350);
        var toggleRestored = notch.IsVisible;
        notch.SetNotchVisible(false); await Task.Delay(60); notch.ToggleVisibility(); await Task.Delay(350);
        var rapidToggleRestored = notch.IsVisible;
        service.Settings.AnimateNotch = false;
        notch.SetNotchVisible(false); notch.SetNotchVisible(true);
        var withoutAnimation = notch.IsVisible;
        File.AppendAllText(Path.Combine(Output, "interaction.txt"), $"toggleHidden={toggleHidden}; toggleRestored={toggleRestored}; rapidToggleRestored={rapidToggleRestored}; withoutAnimation={withoutAnimation}\n");
        service.Settings.NotchAutoHideMinutes = 5;
        typeof(NotchWindow).GetField("_lastInteraction", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(notch, Environment.TickCount64 - 301000);
        typeof(NotchWindow).GetMethod("CheckSystemState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(notch, null);
        var autoHidden = !notch.IsVisible;
        notch.ToggleVisibility();
        var autoHideRestored = notch.IsVisible;
        service.Settings.NotchAutoHideMinutes = 0;
        typeof(NotchWindow).GetField("_lastInteraction", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(notch, Environment.TickCount64 - 601000);
        typeof(NotchWindow).GetMethod("CheckSystemState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(notch, null);
        File.AppendAllText(Path.Combine(Output, "interaction.txt"), $"autoHidden={autoHidden}; autoHideRestored={autoHideRestored}; neverStaysVisible={notch.IsVisible}\n");
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        await Task.Delay(10000);
        var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        File.AppendAllText(Path.Combine(Output, "interaction.txt"), $"idle10sCpuMilliseconds={cpu:0.0}\n");
        notch.Stop(); notch.Close(); focus.Close();
        Directory.Delete(root, true);
    }
    private static CodexContext Fixture(string provider, string name, double five, double week)
    {
        var now = DateTimeOffset.UtcNow;
        return new CodexContext { Provider = provider, Name = name, LastAttemptAt = now, Snapshot = new(now, "visual fixture", null,
            [UsageWindow.FromRemaining(provider.ToLowerInvariant() + ":five_hour", "FIVE_HOUR", "5h", five, now, "visual fixture", now.AddHours(2).AddMinutes(14)), UsageWindow.FromRemaining(provider.ToLowerInvariant() + ":seven_day", "WEEKLY", "Weekly", week, now, "visual fixture", now.AddDays(3))]) };
    }
    private static void Capture(System.Windows.Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Output, name + ".png")); png.Save(file);
    }
    private static T? FindVisual<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T element && element.Name == name) return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindVisual<T>(VisualTreeHelper.GetChild(root, i), name) is { } child) return child;
        return null;
    }
    private static void Click(FrameworkElement element, double x, double y)
    {
        var p = element.PointToScreen(new System.Windows.Point(x, y)); SetCursorPos((int)p.X, (int)p.Y);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
}
