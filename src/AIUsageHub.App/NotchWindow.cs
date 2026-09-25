using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace AIUsageHub;

public sealed class NotchWindow : Window
{
    private readonly HubService _service;
    private readonly Action _openApp, _openSettings;
    private readonly Func<Task> _refreshUsage;
    private readonly StackPanel _content = new() { Margin = new Thickness(12, 6, 12, 12) };
    private readonly DispatcherTimer _systemTimer;
    private IntPtr _hwnd, _mouseHook;
    private readonly MouseHookProc _mouseCallback;
    private bool _stopped;
    private bool _expanded, _hiddenForFullscreen;
    private readonly TranslateTransform _slide = new();
    private bool _manuallyHidden, _hiding;
    private bool _refreshing;
    private long _lastInteraction = Environment.TickCount64;
    private int _animationVersion;
    private double _capsuleWidth = 240;
    public const double CapsuleHeight = 36;
    public const double FlyoutWidth = 480;
    private const string ForegroundColor = "#F3F5FA", Muted = "#A8B5C7";

    public NotchWindow(HubService service, Action openApp, Action openSettings, Func<Task>? refreshUsage = null)
    {
        _service = service; _openApp = openApp; _openSettings = openSettings;
        _refreshUsage = refreshUsage ?? (() => service.RefreshAllAsync(true));
        Title = "AI Pulse Notch";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable, Segoe UI");
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        Content = _content;
        _content.RenderTransform = _slide;
        MouseMove += (_, _) => _lastInteraction = Environment.TickCount64;
        IsVisibleChanged += (_, _) => { if (IsVisible) _lastInteraction = Environment.TickCount64; };
        _mouseCallback = ObserveMouse;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var styles = WindowsInterop.GetWindowLongPtr(_hwnd, WindowsInterop.GWL_EXSTYLE).ToInt64();
            WindowsInterop.SetWindowLongPtr(_hwnd, WindowsInterop.GWL_EXSTYLE, new IntPtr(styles | WindowsInterop.WS_EX_NOACTIVATE | WindowsInterop.WS_EX_TOOLWINDOW));
            HwndSource.FromHwnd(_hwnd)?.AddHook(WindowHook);
            Reposition();
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _expanded) { ToggleExpanded(); e.Handled = true; } };
        _service.Changed += OnChanged;
        _systemTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _systemTimer.Tick += (_, _) => CheckSystemState();
        _systemTimer.Start();
        Closed += (_, _) => Stop();
        Build();
    }

    public void ToggleVisibility() => SetNotchVisible(!IsVisible || _hiding || _manuallyHidden);

    public void SetNotchVisible(bool visible)
    {
        _manuallyHidden = !visible;
        _hiddenForFullscreen = false;
        var version = ++_animationVersion;
        var from = _slide.Y;
        _slide.BeginAnimation(TranslateTransform.YProperty, null);
        _hiding = !visible;
        if (visible)
        {
            var wasVisible = IsVisible;
            if (!wasVisible) { Show(); from = -Height; }
            Reposition();
            if (_expanded && _mouseHook == IntPtr.Zero) _mouseHook = SetWindowsHookEx(14, _mouseCallback, IntPtr.Zero, 0);
            _lastInteraction = Environment.TickCount64;
        }
        else RemoveMouseHook();
        var target = visible ? 0 : -Height;
        if (!_service.Settings.AnimateNotch || !SystemParameters.ClientAreaAnimation)
        {
            _slide.Y = target;
            Finish();
            return;
        }
        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.HoldEnd };
        animation.Completed += (_, _) => { if (version == _animationVersion) Finish(); };
        _slide.BeginAnimation(TranslateTransform.YProperty, animation);
        void Finish()
        {
            _hiding = false;
            if (!visible) { Hide(); if (_expanded) { _expanded = false; Build(); } }
            else CheckSystemState();
        }
    }

    public void ToggleExpanded()
    {
        _lastInteraction = Environment.TickCount64;
        _expanded = !_expanded;
        if (_expanded) _mouseHook = SetWindowsHookEx(14, _mouseCallback, IntPtr.Zero, 0);
        else RemoveMouseHook();
        Build(animate: _expanded);
        if (_expanded) _ = _service.RefreshOldOnOpenAsync();
    }

    public void RefreshPlacement() { Reposition(); CheckSystemState(); }
    public void Stop() { if (_stopped) return; _stopped = true; ++_animationVersion; _slide.BeginAnimation(TranslateTransform.YProperty, null); _systemTimer.Stop(); RemoveMouseHook(); _service.Changed -= OnChanged; }
    private void RemoveMouseHook() { if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
    private void OnChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Build()); return; }
        Build();
    }

    private static Border Surface(double radius) => new()
    {
        Background = new LinearGradientBrush(Color.FromArgb(253, 29, 42, 59), Color.FromArgb(253, 22, 31, 46), 90),
        BorderBrush = UiKit.Solid("#26FFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius),
        Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = .25, Color = Colors.Black }
    };

    private void Build(bool animate = false)
    {
        if (_stopped) return;
        _content.Children.Clear();
        var now = DateTimeOffset.UtcNow;
        var capsule = Surface(18);
        capsule.Height = CapsuleHeight;
        capsule.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        var capsuleContent = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 0) };
        var mark = UiKit.BrandMark(); mark.Margin = new Thickness(0,0,10,0); capsuleContent.Children.Add(mark);
        var accounts = ContextIdentity.VisibleAccounts(_service.Contexts, now);
        foreach (var provider in new[] { "Codex", "Claude" })
        {
            var selection = provider == "Codex" ? _service.Settings.CodexNotchAccountKey : _service.Settings.ClaudeNotchAccountKey;
            var reading = NotchPolicy.CapsuleReading(accounts, provider, selection, now);
            if (reading is null) continue;
            var remaining = reading.LowestRemaining;
            var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = UiKit.Solid(remaining is null ? "#7C8BA2" : remaining <= 15 ? "#F2BC56" : "#7CD665"), Margin = new Thickness(10,0,8,0), VerticalAlignment = VerticalAlignment.Center };
            capsuleContent.Children.Add(dot);
            capsuleContent.Children.Add(Text(provider,12));
            var amount = Text("  " + reading.Text,12,true); amount.Margin = new Thickness(0,0,10,0); capsuleContent.Children.Add(amount);
            dot.ToolTip = amount.ToolTip = reading.ToolTip;
        }
        if (capsuleContent.Children.Count == 1) capsuleContent.Children.Add(Text("AI Pulse",12,true));
        var refresh = new Button
        {
            Name = "NotchRefreshButton", Width = 24, Height = 24, Margin = new Thickness(5, 0, 0, 0),
            Content = RefreshIcon(_refreshing), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(0), Cursor = Cursors.Hand,
            Focusable = false, FocusVisualStyle = null,
            IsEnabled = !_refreshing, ToolTip = _refreshing ? "Refreshing accounts…" : "Refresh all accounts"
        };
        System.Windows.Automation.AutomationProperties.SetName(refresh, "Refresh all accounts");
        var refreshSurface = new FrameworkElementFactory(typeof(Border));
        refreshSurface.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        refreshSurface.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var refreshContent = new FrameworkElementFactory(typeof(ContentPresenter));
        refreshContent.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        refreshContent.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        refreshSurface.AppendChild(refreshContent);
        refresh.Template = new ControlTemplate(typeof(Button)) { VisualTree = refreshSurface };
        refresh.MouseEnter += (_, _) => refresh.Background = UiKit.Solid("#25465D");
        refresh.MouseLeave += (_, _) => refresh.Background = Brushes.Transparent;
        refresh.Click += (_, e) => { e.Handled = true; _ = RefreshFromNotchAsync(); };
        capsuleContent.Children.Add(refresh);
        capsule.Child = capsuleContent;
        capsule.Measure(new Size(double.PositiveInfinity, CapsuleHeight));
        _capsuleWidth = capsule.DesiredSize.Width;
        capsule.Width = _capsuleWidth;
        capsule.Cursor = Cursors.Hand;
        capsule.ToolTip = "AI Pulse · Click to view accounts";
        capsule.MouseEnter += (_, _) => { capsule.Background = UiKit.Solid("#F52A394E"); capsule.BorderBrush = UiKit.Solid("#40FFFFFF"); };
        capsule.MouseLeave += (_, _) => { capsule.Background = Surface(18).Background; capsule.BorderBrush = UiKit.Solid("#26FFFFFF"); capsule.Opacity = 1; };
        capsule.MouseLeftButtonDown += (_, _) => { if (!refresh.IsMouseOver) capsule.Opacity = .85; };
        capsule.MouseLeftButtonUp += (_, _) => { if (!refresh.IsMouseOver) ToggleExpanded(); };
        _content.Children.Add(capsule);

        if (_expanded)
        {
            var panel = Surface(12);
            panel.Width = FlyoutWidth;
            panel.Margin = new Thickness(0, 6, 0, 0);
            var body = new StackPanel { Margin = new Thickness(18, 14, 18, 10) };
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var recent = _service.Contexts.Where(c => c.Provider == "Codex" && Freshness.IsFresh(c, now)).Select(c => c.Snapshot!.FetchedAt).DefaultIfEmpty().Max();
            var age = Text(recent == default ? "Usage overview" : "Updated " + Freshness.Age(recent, now).ToLowerInvariant(), 10, false, Muted);
            DockPanel.SetDock(age, Dock.Right); heading.Children.Add(age);
            heading.Children.Add(Text("AI Pulse", 16, true)); body.Children.Add(heading);
            var list = new StackPanel();

            foreach (var provider in new[] { "Codex", "Claude" })
            {
                var group = accounts.Where(c => c.Provider == provider).OrderBy(c => c.Name).ToArray();
                if (group.Length == 0) continue;
                var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var count = Text($"{group.Length} account{(group.Length == 1 ? "" : "s")}", 11, false, Muted);
                DockPanel.SetDock(count, Dock.Right); header.Children.Add(count);
                var symbol = Text(provider == "Claude" ? "✳" : "◎", 17, false, provider == "Claude" ? "#D9A18B" : "#8CBFAF");
                symbol.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(symbol, Dock.Left); header.Children.Add(symbol);
                header.Children.Add(Text(provider == "Codex" ? "OPENAI / CODEX" : "CLAUDE", 11, true, Muted));
                list.Children.Add(header);
                foreach (var account in group) list.Children.Add(AccountRow(account, now));
            }
            if (accounts.Count == 0) list.Children.Add(Text("Add an account in the app to get started.", 12, false, Muted));
            list.Measure(new Size(FlyoutWidth - 38, double.PositiveInfinity));
            var screen = WindowsInterop.SelectedScreen(_service.Settings.DisplayId);
            var maxHeight = Math.Max(100, Math.Min(510, screen.WorkingArea.Height / WindowsInterop.ScaleFor(screen) - 160));
            body.Children.Add(new ScrollViewer { Content = list, MaxHeight = maxHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            body.Children.Add(new Border { Height = 1, Background = UiKit.Solid("#18FFFFFF"), Margin = new Thickness(0, 3, 0, 7) });
            var footer = new DockPanel();
            var settings = LinkButton("Settings", () => { ToggleExpanded(); _openSettings(); });
            DockPanel.SetDock(settings, Dock.Right); footer.Children.Add(settings);
            footer.Children.Add(LinkButton("Open App", () => { ToggleExpanded(); _openApp(); }));
            body.Children.Add(footer);
            panel.Child = body;
            _content.Children.Add(panel);
            if (animate && _service.Settings.AnimateNotch && SystemParameters.ClientAreaAnimation)
                panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
        Width = (_expanded ? FlyoutWidth : _capsuleWidth) + 24;
        _content.Measure(new Size(Width, double.PositiveInfinity));
        Height = _content.DesiredSize.Height;
        Reposition();
    }

    private async Task RefreshFromNotchAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        Build();
        try { await _refreshUsage(); }
        catch (Exception) { SafeLog.Write(_service.DataDirectory, "PROVIDER_ERROR", "notch-refresh"); }
        finally { _refreshing = false; if (!_stopped) Build(); }
    }

    private FrameworkElement AccountRow(CodexContext context, DateTimeOffset now)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 13) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(31) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var avatar = new Border { Width = 23, Height = 23, CornerRadius = new CornerRadius(12), Background = UiKit.Solid("#334A627F"), VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = string.IsNullOrWhiteSpace(context.Name) ? "?" : context.Name[..1].ToUpperInvariant(), FontSize = 11, Foreground = UiKit.Solid("#DFE7F3"), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        row.Children.Add(avatar);
        var content = new StackPanel(); Grid.SetColumn(content, 1); row.Children.Add(content);
        content.Children.Add(Text(context.Name, 12, true));
        if (context.Snapshot is { } snapshot)
        {
            foreach (var window in snapshot.Windows) content.Children.Add(UsageRow(window, now));
            var state = Freshness.IsFresh(context, now) ? "" : " · stale";
            var age = Text($"{(context.Provider == "Claude" ? "Last observed" : "Updated")} {Freshness.Age(snapshot.FetchedAt, now).ToLowerInvariant()}{state}", 10, false, Muted);
            age.Margin = new Thickness(0, 4, 0, 0); content.Children.Add(age);
        }
        if (context.ErrorCode is not null || context.Snapshot is null)
        {
            var status = Text(context.ErrorCode is null ? Freshness.DisplayState(context, now) : Freshness.ErrorText(context.ErrorCode), 11, false, Muted);
            if (context.Provider != "Claude" && context.ErrorCode is null) status.Text = "Waiting for usage data";
            status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 4, 0, 0); content.Children.Add(status);
            if (context.Provider == "Claude" && context.ErrorCode is null)
            {
                var hint = Text("Hub checks shared Claude plan usage automatically.", 10, false, Muted);
                hint.Margin = new Thickness(0, 4, 0, 0); content.Children.Add(hint);
            }
        }
        if (context.Provider == "Claude" && (context.ErrorCode is "CLAUDE_AUTH_REQUIRED" or "CLAUDE_LOGIN_FAILED" || context.SigningIn))
        {
            var login = LinkButton(context.SigningIn ? "Sign-in open in Claude Code" : "Sign in with Claude Code", () => _ = _service.SignInClaudeAsync(context));
            login.IsEnabled = !context.SigningIn; login.Margin = new Thickness(-6, 4, 0, 0); content.Children.Add(login);
        }
        return row;
    }

    private static FrameworkElement UsageRow(UsageWindow window, DateTimeOffset now)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        foreach (var width in new[] { new GridLength(64), new GridLength(1, GridUnitType.Star), new GridLength(68), new GridLength(111) })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        var label = Text(window.Kind switch { "FIVE_HOUR" => "5h", "WEEKLY" => window.Id.StartsWith("codex:") || window.Id.StartsWith("claude:") ? "Weekly" : window.Title.Replace(" Weekly", ""), "CREDITS" => "Credits", _ => window.Title }, 11, false, Muted);
        label.ToolTip = window.Title; grid.Children.Add(label);
        var expired = window.ResetsAt <= now;
        var remaining = expired ? null : window.RemainingPercent;
        var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = UiKit.Solid("#344358"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 8, 0) };
        if (remaining is { } percent)
        {
            var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Background = UiKit.Solid(percent <= 5 ? "#E67E83" : percent <= 15 ? "#E4B469" : "#7EA8E8") };
            track.Child = fill;
            track.SizeChanged += (_, _) => fill.Width = track.ActualWidth * percent / 100;
        }
        Grid.SetColumn(track, 1); grid.Children.Add(track);
        var amount = Text(remaining is not null ? $"{UsageWindow.FormatPercent(remaining)} left" : !expired && window.Remaining is not null ? $"{window.Remaining:0.##}" : "—", 11, true);
        Grid.SetColumn(amount, 2); grid.Children.Add(amount);
        var reset = Text(expired ? "Awaiting update" : ResetText(window.ResetsAt, now), 10, false, Muted);
        reset.ToolTip = window.ResetsAt?.ToLocalTime().ToString("F"); Grid.SetColumn(reset, 3); grid.Children.Add(reset);
        return grid;
    }

    private static string ResetText(DateTimeOffset? reset, DateTimeOffset now)
    {
        if (reset is null) return "";
        var span = reset.Value - now;
        return span.TotalHours < 24 ? $"Resets in {(int)span.TotalHours}h {span.Minutes}m" : $"Resets {reset.Value.ToLocalTime():ddd HH:mm}";
    }
    private static FrameworkElement RefreshIcon(bool busy) => new System.Windows.Shapes.Path
    {
        Name = "NotchRefreshIcon", Width = 16, Height = 16,
        Data = Geometry.Parse("M 13.2,4.8 A 5.5,5.5 0 1 0 13.5,9.8 M 13.2,1.8 L 13.2,4.8 L 10.2,4.8"),
        Stretch = Stretch.None, Stroke = UiKit.Solid(busy ? Muted : ForegroundColor),
        StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
    };
    private static TextBlock Text(string value, double size, bool bold = false, string color = ForegroundColor) => new()
    { Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = UiKit.Solid(color), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

    private static Button LinkButton(string label, Action action)
    {
        var button = new Button { Content = label, Foreground = UiKit.Solid("#C5D8F5"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Padding = new Thickness(6, 4, 6, 4), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, FontSize = 11 };
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 4, 6, 4)); border.AppendChild(presenter);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        button.MouseEnter += (_, _) => button.Background = UiKit.Solid("#16FFFFFF"); button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.Click += (_, _) => action(); return button;
    }

    public void Reposition()
    {
        if (_hwnd == IntPtr.Zero || WinForms.Screen.AllScreens.Length == 0) return;
        var screen = WindowsInterop.SelectedScreen(_service.Settings.DisplayId);
        var scale = WindowsInterop.ScaleFor(screen);
        var width = Math.Min((int)Math.Round(Width * scale), screen.Bounds.Width - 16);
        var height = Math.Min((int)Math.Round(Height * scale), screen.Bounds.Height - 16);
        WindowsInterop.SetWindowPos(_hwnd, _service.Settings.AlwaysOnTop ? WindowsInterop.HWND_TOPMOST : WindowsInterop.HWND_NOTOPMOST,
            screen.Bounds.Left + (screen.Bounds.Width - width) / 2, screen.Bounds.Top, width, height, WindowsInterop.SWP_NOACTIVATE);
    }
    private void CheckSystemState()
    {
        if (_hwnd == IntPtr.Zero) return;
        var minutes = _service.Settings.NotchAutoHideMinutes;
        if (IsVisible && !_hiding && minutes > 0 && Environment.TickCount64 - _lastInteraction >= TimeSpan.FromMinutes(minutes).TotalMilliseconds)
        { SetNotchVisible(false); return; }
        var hide = _service.Settings.AutoHideFullscreen && WindowsInterop.HasFullscreenForeground(WindowsInterop.SelectedScreen(_service.Settings.DisplayId), _hwnd);
        if (hide && IsVisible) { if (_expanded) ToggleExpanded(); _hiddenForFullscreen = true; Hide(); }
        else if (!hide && _hiddenForFullscreen && !_manuallyHidden) { _hiddenForFullscreen = false; Show(); Reposition(); }
    }
    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return new IntPtr(3); }
        if (message is 0x007E or 0x02E0 or 0x001A) Dispatcher.BeginInvoke(() => { Reposition(); });
        return IntPtr.Zero;
    }
    private IntPtr ObserveMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && _expanded && message.ToInt64() is 0x0201 or 0x0204 or 0x0207)
        {
            var point = Marshal.PtrToStructure<WindowsInterop.Point>(data);
            // Queue work so the system hook always returns immediately; never swallow the click.
            Dispatcher.BeginInvoke(() =>
            {
                if (_stopped || !_expanded || !IsVisible) return;
                var local = PointFromScreen(new System.Windows.Point(point.X, point.Y));
                var onCapsule = local.Y >= 6 && local.Y <= 42 && Math.Abs(local.X - Width / 2) <= _capsuleWidth / 2;
                var onPanel = local.X >= 12 && local.X <= Width - 12 && local.Y >= 48 && local.Y <= Height - 12;
                if (!onCapsule && !onPanel) ToggleExpanded();
            });
        }
        return CallNextHookEx(_mouseHook, code, message, data);
    }
    private delegate IntPtr MouseHookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, MouseHookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
}

public static class NotchPolicy
{
    public sealed record CapsuleValue(CodexContext Account, string Text, double? LowestRemaining, string ToolTip);

    public static CapsuleValue? CapsuleReading(IEnumerable<CodexContext> contexts, string provider, string? selection, DateTimeOffset now)
    {
        if (selection == HubSettings.HiddenNotchAccount) return null;
        var accounts = ContextIdentity.VisibleAccounts(contexts, now).Where(c => c.Provider == provider).ToArray();
        var account = accounts.FirstOrDefault(c => ContextIdentity.AccountKey(c) == selection) ??
            accounts.OrderByDescending(c => Freshness.IsFresh(c, now)).ThenByDescending(c => c.IsDefault)
                .ThenByDescending(c => c.Snapshot?.FetchedAt ?? DateTimeOffset.MinValue).ThenBy(c => c.Name).FirstOrDefault();
        if (account is null) return null;
        if (account.Snapshot is null || account.ErrorCode is not null || now - account.Snapshot.FetchedAt > TimeSpan.FromMinutes(15))
            return new CapsuleValue(account, "—", null, $"{account.Name} · {Freshness.DisplayState(account, now)}");

        var available = account.Snapshot.Windows.Where(w => w.RemainingPercent is not null && (w.ResetsAt is null || w.ResetsAt > now)).ToArray();
        UsageWindow? Pick(string kind) => available.Where(w => w.Kind == kind)
            .OrderByDescending(w => w.Id.StartsWith("codex:", StringComparison.OrdinalIgnoreCase) || w.Id.StartsWith("claude:", StringComparison.OrdinalIgnoreCase))
            .ThenBy(w => w.Id).FirstOrDefault();
        var fiveHour = Pick("FIVE_HOUR");
        var weekly = Pick("WEEKLY");
        var shown = new[] { fiveHour, weekly }.Where(w => w is not null).ToArray();
        if (shown.Length == 0) return new CapsuleValue(account, "—", null, $"{account.Name} · No current 5-hour or weekly limit");
        var text = shown.Length == 2
            ? $"{UsageWindow.FormatPercent(fiveHour!.RemainingPercent).TrimEnd('%')}/{UsageWindow.FormatPercent(weekly!.RemainingPercent)}"
            : UsageWindow.FormatPercent(shown[0]!.RemainingPercent);
        var tooltip = account.Name + " · " + string.Join(" · ", shown.Select(w =>
            $"{(w!.Kind == "FIVE_HOUR" ? "5-hour" : "Weekly")} {UsageWindow.FormatPercent(w.RemainingPercent)} left"));
        return new CapsuleValue(account, text, shown.Min(w => w!.RemainingPercent), tooltip);
    }

    public static double? LowestRemaining(IEnumerable<CodexContext> contexts, DateTimeOffset now)
        => contexts.Where(c => Freshness.IsFresh(c, now)).SelectMany(c => c.Snapshot!.Windows)
            .Where(w => w.ResetsAt is null || w.ResetsAt > now).Select(w => w.RemainingPercent).Min();

    public static string Summary(IEnumerable<CodexContext> contexts, DateTimeOffset now)
    {
        var accounts = ContextIdentity.VisibleAccounts(contexts, now);
        var valid = accounts.Where(c => Freshness.IsFresh(c, now)).SelectMany(c => c.Snapshot!.Windows
            .Where(w => w.RemainingPercent is not null && (w.ResetsAt is null || w.ResetsAt > now)).Select(w => (Context: c, Window: w))).ToArray();
        var warning = valid.Where(x => x.Window.RemainingPercent <= 15).OrderBy(x => x.Window.RemainingPercent).FirstOrDefault();
        if (warning.Window is not null) return $"{warning.Context.Provider} {warning.Context.Name}  {UsageWindow.FormatPercent(warning.Window.RemainingPercent)} left";
        var providers = accounts.Select(c => c.Provider).Distinct().OrderBy(p => p == "Codex" ? 0 : 1);
        var parts = providers.Select(provider =>
        {
            var windows = valid.Where(x => x.Context.Provider == provider).ToArray();
            return windows.Length == 0 ? provider + " —" : $"{provider} {UsageWindow.FormatPercent(windows.Min(x => x.Window.RemainingPercent))}";
        }).ToArray();
        return parts.Length == 0 ? "AI Pulse" : string.Join("   ·   ", parts);
    }
}
