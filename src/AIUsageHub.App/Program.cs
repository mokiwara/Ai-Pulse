using System.Drawing;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace AIUsageHub;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--claude-statusline")
        {
            try
            {
                using var inputTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                // Console.In may implement ReadAsync with a blocking read that ignores cancellation.
                // Keep it on a background thread and bound the helper's main-thread wait as well.
                var input = Task.Run(() => BoundedText.ReadAsync(Console.In, 2_000_000, inputTimeout.Token))
                    .WaitAsync(inputTimeout.Token).GetAwaiter().GetResult();
                var line = ClaudeConnector.CaptureStatusLine(args[1], input);
                if (line is not null) Console.WriteLine(line);
            }
            catch { /* A status line must never interrupt the user's Claude session. */ }
            return;
        }
        if (args.Contains("--exit", StringComparer.OrdinalIgnoreCase))
        {
            try { using var running = EventWaitHandle.OpenExisting(@"Local\AIUsageHub.Exit"); running.Set(); }
            catch { }
            return;
        }
        using var mutex = new Mutex(true, @"Local\AIUsageHub.SingleInstance", out var first);
        if (!first)
        {
            try { using var existing = EventWaitHandle.OpenExisting(@"Local\AIUsageHub.OpenWindow"); existing.Set(); }
            catch { }
            return;
        }
        if (args.Contains("--uninstall-cleanup", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                StartupRegistration.RemoveCurrentExecutable();
                foreach (var context in new LocalStore().LoadContexts().Where(c => c.Provider == "Claude"))
                    ClaudeConnector.RemoveStatusLine(context, currentExecutableOnly: true);
            }
            catch { Environment.ExitCode = 1; }
            return;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        HubHost? host = null;
        app.DispatcherUnhandledException += (_, e) =>
        {
            try { SafeLog.Write(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsageHub"), "PROVIDER_ERROR", "ui"); }
            catch { }
            MessageBox.Show("AI Pulse encountered an error. See the local log for details.", "AI Pulse", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
        app.Startup += (_, _) =>
        {
            try { host = new HubHost(app); host.Start(); }
            catch
            {
                MessageBox.Show("AI Pulse could not start. Check that your local data folder is accessible and try again. Your data has not been reset.", "AI Pulse", MessageBoxButton.OK, MessageBoxImage.Error);
                host?.Dispose(); app.Shutdown(1);
            }
        };
        app.Exit += (_, _) => host?.Dispose();
        app.Run();
    }
}

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AIUsageHub";
    public static void RemoveCurrentExecutable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string command && command.StartsWith($"\"{Environment.ProcessPath}\"", StringComparison.OrdinalIgnoreCase)) key.DeleteValue(ValueName, false);
    }

    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable)) return false;
                var command = $"\"{executable}\"";
                if (Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                    command += $" \"{Path.Combine(AppContext.BaseDirectory, "AIUsageHub.App.dll")}\"";
                key?.SetValue(ValueName, command);
            }
            else key?.DeleteValue(ValueName, false);
            return true;
        }
        catch { return false; }
    }
}

public sealed class HubHost : IDisposable
{
    private readonly Application _app;
    private readonly HubService _service;
    private readonly MainWindow _main;
    private readonly NotchWindow _notch;
    private readonly WinForms.NotifyIcon _tray;
    private readonly Icon _trayIcon;
    private readonly EventWaitHandle _openSignal;
    private readonly EventWaitHandle _exitSignal;
    private readonly ManualResetEvent _signalStop = new(false);
    private readonly Task _signalTask;
    private bool _disposed;
    private bool _exiting;
    private readonly DoubleAltShortcut? _shortcut;

    public HubHost(Application app)
    {
        _app = app;
        _service = new HubService(new LocalStore());
        if (_service.Contexts.Count == 0 && !_service.DefaultHomeWasRemoved()) _service.DetectDefault();
        _main = new MainWindow(_service, Exit, ToggleNotch);
        _notch = new NotchWindow(_service, () => _main.Open(), () => _main.Open("Settings"));
        try { _shortcut = new DoubleAltShortcut(() => { if (!_exiting) _app.Dispatcher.BeginInvoke(ToggleNotch); }); }
        catch (System.ComponentModel.Win32Exception) { SafeLog.Write(_service.DataDirectory, "PROVIDER_ERROR", "shortcut-unavailable"); }
        var iconPath = Environment.ProcessPath ?? "";
        _trayIcon = File.Exists(iconPath) ? Icon.ExtractAssociatedIcon(iconPath) ?? (Icon)SystemIcons.Application.Clone() : (Icon)SystemIcons.Application.Clone();
        _tray = new WinForms.NotifyIcon { Icon = _trayIcon, Text = "AI Pulse", Visible = true };
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open AI Pulse", null, (_, _) => _main.Open());
        menu.Items.Add("Refresh now", null, (_, _) => _ = _service.RefreshAllAsync(true));
        menu.Items.Add("Show / Hide Notch", null, (_, _) => ToggleNotch());
        menu.Items.Add("Settings", null, (_, _) => _main.Open("Settings"));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => _main.Open();
        _service.Notice += ShowNotice;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.SessionSwitch += SessionChanged;
        _openSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AIUsageHub.OpenWindow");
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AIUsageHub.Exit");
        _signalTask = Task.Run(() =>
        {
            while (true)
            {
                var action = WaitHandle.WaitAny([_signalStop, _openSignal, _exitSignal]);
                if (action == 0) return;
                if (action == 1) _app.Dispatcher.BeginInvoke(() => { if (!_exiting) _main.Open(); });
                else if (action == 2) _app.Dispatcher.BeginInvoke(Exit);
            }
        });
    }

    public void Start()
    {
        if (_service.Settings.StartWithWindows) StartupRegistration.Set(true);
        if (_shortcut is null) _tray.ShowBalloonTip(6000, "AI Pulse", "Double-Alt could not be enabled. Use the tray menu to show or hide the notch.", WinForms.ToolTipIcon.Info);
        if (_service.Settings.ShowNotchOnStartup) _notch.SetNotchVisible(true);
        if (!_service.Settings.StartMinimized) _main.Open();
        _service.Start();
    }

    private void ToggleNotch()
    {
        if (!_exiting) _notch.ToggleVisibility();
    }

    private void ShowNotice(HubNotice notice)
    {
        _tray.BalloonTipTitle = notice.Title;
        _tray.BalloonTipText = notice.Body;
        _tray.ShowBalloonTip(6000);
    }

    private void DisplayChanged(object? sender, EventArgs e) => _app.Dispatcher.BeginInvoke(_notch.RefreshPlacement);
    private void PowerChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) _app.Dispatcher.BeginInvoke(() => { _notch.RefreshPlacement(); _service.Resume(); });
    }
    private void SessionChanged(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock) _app.Dispatcher.BeginInvoke(() => { _notch.RefreshPlacement(); _service.Resume(); });
    }

    public async void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        _main.IsEnabled = false;
        _shortcut?.Dispose();
        _service.Dispose();
        try { await _service.Stopped.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { SafeLog.Write(_service.DataDirectory, "TIMEOUT", "shutdown"); }
        _main.AllowClose = true;
        _notch.Stop();
        _tray.Visible = false;
        _main.Close();
        _notch.Close();
        _app.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shortcut?.Dispose();
        _signalStop.Set();
        _signalTask.GetAwaiter().GetResult();
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        SystemEvents.PowerModeChanged -= PowerChanged;
        SystemEvents.SessionSwitch -= SessionChanged;
        _service.Notice -= ShowNotice;
        _service.Dispose();
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _trayIcon.Dispose();
        _openSignal.Dispose();
        _exitSignal.Dispose();
        _signalStop.Dispose();
    }
}
