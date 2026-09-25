using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;

namespace AIUsageHub;

public sealed class MainWindow : Window
{
    private readonly HubService _service;
    private readonly Action _exit;
    private readonly Action _toggleNotch;
    private UiKit _ui;
    private readonly DockPanel _root = new();
    private string _page = "Overview";
    private string _renderedPage = "Overview";
    private readonly Dictionary<string, double> _scrollOffsets = new();
    private bool _closed;
    public bool AllowClose { get; set; }

    public MainWindow(HubService service, Action exit, Action toggleNotch)
    {
        _service = service; _exit = exit; _toggleNotch = toggleNotch;
        _ui = new UiKit(service.Settings.Theme);
        Title = "AI Pulse"; Width = 1240; Height = 850; MinWidth = 1000; MinHeight = 620;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable, Segoe UI");
        UseLayoutRounding = true;
        WindowStyle = WindowStyle.None;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 36, ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(12), GlassFrameThickness = new Thickness(0) });
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/AIUsageHub.App;component/Assets/icon.ico"));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = _root;
        _service.Changed += OnChanged;
        Closed += (_, _) => { _closed = true; _service.Changed -= OnChanged; };
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
        Render();
    }

    public void Open(string page = "Overview")
    {
        _page = page;
        Render();
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnChanged()
    {
        if (_closed) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Render); return; }
        Render();
    }

    private void Render()
    {
        if (_closed) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Render); return; }
        _ui = new UiKit(_service.Settings.Theme);
        Background = _ui.Background;
        if (_root.Children.OfType<ScrollViewer>().FirstOrDefault() is { } previousScroll)
            _scrollOffsets[_renderedPage] = previousScroll.VerticalOffset;
        _root.Children.Clear();
        _ui.InstallResources(this);
        var chrome = new DockPanel { Height = 36, LastChildFill = false };
        DockPanel.SetDock(chrome, Dock.Top);
        foreach (var (label, action) in new (string, Action)[] { ("\u00D7", () => Close()), ("\u25A1", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized), ("\u2212", () => WindowState = WindowState.Minimized) })
        {
            var control = _ui.Button(label, action); control.Width = 44; control.Margin = new Thickness(0); control.Padding = new Thickness(0); control.Background = Brushes.Transparent; control.BorderThickness = new Thickness(0);
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(control, true);
            DockPanel.SetDock(control, Dock.Right); chrome.Children.Add(control);
        }
        var sidebar = new DockPanel { Margin = new Thickness(16, 12, 16, 20) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 28) };
        brand.Children.Add(UiKit.BrandMark(1.3));
        var brandText = _ui.Text("AI Pulse", 16, true); brandText.Margin = new Thickness(12,0,0,0); brand.Children.Add(brandText);
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var foot = new StackPanel { Margin = new Thickness(10) };
        foot.Children.Add(_ui.Text("LOCAL WORKSPACE", 10, true, _ui.Muted));
        foot.Children.Add(Spacer(10));
        foot.Children.Add(_ui.Text($"{_service.Contexts.Count} connected contexts", 12, false, _ui.Muted));
        foot.Children.Add(Spacer(18));
        foot.Children.Add(_ui.Text("Alt  +  Alt", 14, true));
        foot.Children.Add(_ui.Text("Show or hide your notch", 12, false, _ui.Muted));
        DockPanel.SetDock(foot, Dock.Bottom); sidebar.Children.Add(foot);
        var nav = new StackPanel();
        foreach (var (page, symbol) in new[] { ("Overview", "\uE80F"), ("Accounts", "\uE77B"), ("Settings", "\uE713") })
        {
            var button = _ui.Button(symbol + "    " + page, () => { _page = page; Render(); }, _page == page);
            var navContent = new StackPanel { Orientation = Orientation.Horizontal };
            var navIcon = Glyph(symbol,20); if (_page == page) navIcon.Foreground = Brushes.White; navContent.Children.Add(navIcon);
            var navLabel = _ui.Text(page,14); if (_page == page) navLabel.Foreground = Brushes.White; navLabel.Margin = new Thickness(16,0,0,0); navContent.Children.Add(navLabel); button.Content = navContent;
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
            button.Padding = new Thickness(16, 13, 16, 13); button.Margin = new Thickness(0, 0, 0, 8);
            if (_page != page) { button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); }
            nav.Children.Add(button);
        }
        sidebar.Children.Add(nav);
        var rail = new Border { Width = 218, Background = _ui.Dark ? UiKit.Solid("#60071020") : UiKit.Solid("#EEF2F9"), BorderBrush = _ui.Border, BorderThickness = new Thickness(0,0,1,0), Child = sidebar };
        DockPanel.SetDock(rail, Dock.Left); _root.Children.Add(rail);
        _root.Children.Add(chrome);
        var header = new DockPanel { Margin = new Thickness(30, 22, 30, 26) };
        DockPanel.SetDock(header, Dock.Top);
        var refresh = _ui.Button("\u21BB   Refresh now", () => _ = _service.RefreshAllAsync(true), true);
        refresh.VerticalAlignment = VerticalAlignment.Center; refresh.Padding = new Thickness(22,12,22,12);
        DockPanel.SetDock(refresh, Dock.Right); header.Children.Add(refresh);
        var title = new StackPanel();
        title.Children.Add(_ui.Text(_page == "Overview" ? "AI Pulse" : _page, 32, true));
        title.Children.Add(Spacer(5));
        title.Children.Add(_ui.Text(_page switch { "Accounts" => "Manage your local OpenAI and Claude contexts", "Settings" => "Make AI Pulse feel at home", _ => "Your AI workspace, at a glance" }, 14, false, _ui.Muted));
        header.Children.Add(title); _root.Children.Add(header);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var body = new StackPanel { Margin = new Thickness(26, 0, 26, 20) };
        scroll.Content = body;
        _root.Children.Add(scroll);
        switch (_page)
        {
            case "Accounts": BuildAccounts(body); break;
            case "Settings": BuildSettings(body); break;
            default: BuildOverview(body); break;
        }
        _renderedPage = _page;
        var offset = _scrollOffsets.GetValueOrDefault(_page);
        scroll.Loaded += (_, _) => scroll.ScrollToVerticalOffset(offset);
    }

    private void BuildOverview(StackPanel body)
    {
        var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0,0,-10,12) };
        var now = DateTimeOffset.UtcNow;
        var near = _service.Contexts.Count(c => Freshness.IsFresh(c, now) && c.Snapshot!.Windows.Any(w => w.RemainingPercent <= 30));
        var fresh = _service.Contexts.Count(c => Freshness.IsFresh(c, now));
        foreach (var (value, label, detail) in new[] { (_service.Contexts.Count.ToString(), "Connected contexts", "Your local accounts"), (near.ToString(), "Near limit", "30% or less remaining"), (fresh.ToString(), "Up to date", "Fresh usage readings"), (_service.Contexts.Select(c => c.Provider).Distinct().Count().ToString(), "Providers", "OpenAI \u00B7 Claude") })
        {
            var content = new StackPanel(); content.Children.Add(_ui.Text(value,26,true)); content.Children.Add(Spacer(6)); content.Children.Add(_ui.Text(label,13)); content.Children.Add(Spacer(4)); content.Children.Add(_ui.Text(detail,11,false,_ui.Muted));
            var statRow = new DockPanel();
            var badge = IconBadge(label switch { "Connected contexts" => "\uE753", "Near limit" => "\uE7BA", "Up to date" => "\uE823", _ => "\uE80A" });
            badge.Margin = new Thickness(0,0,14,0); DockPanel.SetDock(badge,Dock.Left); statRow.Children.Add(badge); statRow.Children.Add(content);
            var card = _ui.Panel(statRow); card.Margin = new Thickness(0,0,10,0); stats.Children.Add(card);
        }
        body.Children.Add(stats);
        if (_service.Contexts.Count == 0)
        {
            var empty = new StackPanel();
            empty.Children.Add(_ui.Text("No provider contexts yet", 17, true));
            empty.Children.Add(_ui.Text("Detect a default context or add an existing one in Accounts.", 13, false, _ui.Muted));
            empty.Children.Add(Spacer(12));
            empty.Children.Add(_ui.Button("Go to Accounts", () => { _page = "Accounts"; Render(); }, true));
            body.Children.Add(_ui.Panel(empty));
            return;
        }
        var visible = ContextIdentity.VisibleAccounts(_service.Contexts, DateTimeOffset.UtcNow);
        foreach (var provider in new[] { "Codex", "Claude" })
        {
            var group = visible.Where(c => c.Provider == provider).OrderBy(c => c.Name).ToArray();
            if (group.Length == 0) continue;
            var section = _ui.Text(provider == "Claude" ? "\u2733   Claude" : "\u25CE   OpenAI / Codex", 21, true);
            section.Margin = new Thickness(0, 8, 0, 10);
            var providerPanel = new StackPanel(); providerPanel.Children.Add(section);
            foreach (var context in group) providerPanel.Children.Add(AccountCard(context));
            body.Children.Add(_ui.Panel(providerPanel, new Thickness(16,8,16,4)));
        }
    }

    private Border AccountCard(CodexContext context)
    {
        var stack = new StackPanel();
        var row = new DockPanel();
        var status = _ui.Text(Freshness.DisplayState(context, DateTimeOffset.UtcNow), 12, false,
            Freshness.IsFresh(context, DateTimeOffset.UtcNow) ? UiKit.Solid("#3AAE83") : UiKit.Solid("#D49547"));

        row.Children.Add(_ui.Text(context.Name, 17, true));
        stack.Children.Add(row);
        var duplicate = _service.Contexts.FirstOrDefault(c => c != context && c.Provider == context.Provider && c.IdentityFingerprint is not null && c.IdentityFingerprint == context.IdentityFingerprint);
        if (duplicate is not null) stack.Children.Add(_ui.Text($"Same {context.Provider} account as {duplicate.Name}", 11, false, _ui.Muted));
        if (context.Snapshot is { } snapshot)
        {
            if (snapshot.Plan is { Length: > 0 })
                stack.Children.Add(_ui.Text($"{context.Provider} · {snapshot.Plan}", 12, false, _ui.Muted));
            foreach (var window in snapshot.Windows)
            {
                var expired = window.ResetsAt is not null && window.ResetsAt <= DateTimeOffset.UtcNow;
                var metric = new Grid { Margin = new Thickness(0,4,0,6) };
                metric.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                metric.ColumnDefinitions.Add(new ColumnDefinition());
                metric.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                metric.Children.Add(_ui.Text(window.Title.Replace("Codex ", ""),12,true));
                var center = new StackPanel { Margin = new Thickness(0,0,16,0) };
                if (!expired && window.RemainingPercent is not null) center.Children.Add(RemainingBar(window));
                if (expired) center.Children.Add(_ui.Text("Awaiting update after reset",11,false,_ui.Muted));
                else if (window.ResetsAt is { } reset) center.Children.Add(_ui.Text($"Resets {reset.ToLocalTime():ddd d MMM, HH:mm}",11,false,_ui.Muted));
                Grid.SetColumn(center,1); metric.Children.Add(center);
                var amount = _ui.Text(expired ? "\u2014" : window.RemainingPercent is not null ? $"{UsageWindow.FormatPercent(window.RemainingPercent)} left" : $"{window.Remaining:0.##} {window.Unit}",12,true);
                Grid.SetColumn(amount,2); metric.Children.Add(amount); stack.Children.Add(metric);
            }
            var age = _ui.Text($"Updated {Freshness.Age(snapshot.FetchedAt, DateTimeOffset.UtcNow).ToLowerInvariant()}",10,false,_ui.Muted);
            age.ToolTip = snapshot.Source; stack.Children.Add(age);
        }
        else
        {
            stack.Children.Add(Spacer(8));
            stack.Children.Add(_ui.Text(context.ErrorCode is null ? Freshness.DisplayState(context, DateTimeOffset.UtcNow) : Freshness.ErrorText(context.ErrorCode), 13, false, _ui.Muted));
        }
        if (context.Provider == "Claude")
        {
            if (context.ErrorCode == "CLAUDE_AUTH_REQUIRED" || context.SigningIn || context.ErrorCode == "CLAUDE_LOGIN_FAILED")
            {
                var signIn = _ui.Button(context.SigningIn ? "Sign-in open in Claude Code" : "Sign in with Claude Code", () => _ = _service.SignInClaudeAsync(context), true);
                signIn.IsEnabled = !context.SigningIn;
                stack.Children.Add(signIn);
            }
            else if (context.Snapshot is null && context.ErrorCode is null)
            {
                stack.Children.Add(_ui.Text("Hub checks your shared Claude plan usage automatically. If it is still empty, use Check updates in Accounts.", 12, false, _ui.Muted));
                stack.Children.Add(_ui.Button("Open Claude usage", () => Process.Start(new ProcessStartInfo("https://claude.ai/settings/usage") { UseShellExecute = true })));
            }
        }
        var identity = new StackPanel { Margin = new Thickness(0,6,20,0) };
        var heading = stack.Children[0]; stack.Children.RemoveAt(0); identity.Children.Add(heading);
        identity.Children.Add(Spacer(8)); identity.Children.Add(status);
        if (stack.Children.Count > 0 && stack.Children[0] is TextBlock plan && plan.Text.StartsWith(context.Provider + " \u00B7")) { stack.Children.RemoveAt(0); identity.Children.Add(plan); }
        var layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(235) }); layout.ColumnDefinitions.Add(new ColumnDefinition());
        var identityRow = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var avatar = IconBadge(context.Name.EndsWith("cli", StringComparison.OrdinalIgnoreCase) ? "\uE756" : "\uE77B"); avatar.Margin = new Thickness(0,0,14,0); DockPanel.SetDock(avatar,Dock.Left); identityRow.Children.Add(avatar); identityRow.Children.Add(identity);
        layout.Children.Add(identityRow); Grid.SetColumn(stack,1); layout.Children.Add(stack);
        var card = _ui.Panel(layout, new Thickness(18,10,18,14));
        card.Background = _ui.Dark ? UiKit.Solid("#50102032") : _ui.Background;
        return card;
    }

    private void BuildAccounts(StackPanel body)
    {
        body.Children.Add(_ui.Text("OPENAI / CODEX", 18, true));
        body.Children.Add(_ui.Text("Hub scans conventional local Codex homes. You can also add an existing context elsewhere. This never signs in or changes another Codex session.", 13, false, _ui.Muted));
        if (_service.DiscoveryStatus.Length > 0) body.Children.Add(_ui.Text(_service.DiscoveryStatus, 12, false, _ui.Muted));
        body.Children.Add(Spacer(14));
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(_ui.Button("Detect default", () =>
        {
            if (_service.DetectDefault() is null) MessageBox.Show(this, "The default Codex directory was not found.", "AI Pulse");
        }));
        actions.Children.Add(_ui.Button("Scan local contexts", () => _ = _service.DiscoverExistingContextsAsync()));
        actions.Children.Add(_ui.Button("Add existing context", AddExistingContext, true));
        body.Children.Add(actions);
        body.Children.Add(Spacer(18));
        foreach (var context in _service.Contexts.Where(c => c.Provider == "Codex").OrderBy(c => c.Name))
        {
            var stack = new StackPanel();
            stack.Children.Add(_ui.Text(context.Name + (context.IsDefault ? " · default" : ""), 16, true));
            stack.Children.Add(_ui.Text(context.HomePath, 12, false, _ui.Muted));
            if (ContextIdentity.HasVerifiedDuplicate(_service.Contexts.Where(c => c != context), context))
                stack.Children.Add(_ui.Text("Same provider account as another context; shown once on Overview.", 12, false, _ui.Muted));
            stack.Children.Add(Spacer(7));
            stack.Children.Add(_ui.Text(Freshness.DisplayState(context, DateTimeOffset.UtcNow), 12, false, _ui.Muted));
            if (context.ErrorCode is not null) stack.Children.Add(_ui.Text(Freshness.ErrorText(context.ErrorCode), 12, false, _ui.Muted));
            if (context.Snapshot is not null)
                stack.Children.Add(_ui.Text($"Last success {Freshness.Age(context.Snapshot.FetchedAt, DateTimeOffset.UtcNow)}", 12, false, _ui.Muted));
            if (context.NextRefreshAt is not null)
                stack.Children.Add(_ui.Text($"Next check {context.NextRefreshAt.Value.ToLocalTime():g}", 12, false, _ui.Muted));
            stack.Children.Add(Spacer(12));
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(_ui.Button("Rename", () => Rename(context)));
            buttons.Children.Add(_ui.Button("Test / Refresh", () => _ = _service.RefreshContextAsync(context, true)));
            buttons.Children.Add(_ui.Button("Remove from AI Pulse", () =>
            {
                if (MessageBox.Show(this, $"Remove {context.Name} from AI Pulse? Codex files will stay untouched.", "Remove context", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    _service.RemoveContext(context);
            }));
            stack.Children.Add(buttons);
            body.Children.Add(ContextManagementCard(stack));
        }
        body.Children.Add(Spacer(14));
        body.Children.Add(_ui.Text("CLAUDE", 18, true));
        body.Children.Add(_ui.Text("Uses an existing Claude Code login. Hub checks shared plan usage with Claude Code's /usage command and also accepts terminal status-line updates.", 13, false, _ui.Muted));
        body.Children.Add(Spacer(10));
        var claudeActions = new WrapPanel { Orientation = Orientation.Horizontal };
        claudeActions.Children.Add(_ui.Button("Detect default", () =>
        {
            if (_service.DetectDefaultClaude() is null) MessageBox.Show(this, "The default Claude directory was not found.", "AI Pulse");
        }));
        claudeActions.Children.Add(_ui.Button("Scan local contexts", () => _ = _service.DiscoverClaudeContextsAsync()));
        claudeActions.Children.Add(_ui.Button("Add existing context", AddExistingClaudeContext, true));
        claudeActions.Children.Add(_ui.Button("Open Claude usage", () => Process.Start(new ProcessStartInfo("https://claude.ai/settings/usage") { UseShellExecute = true })));
        body.Children.Add(claudeActions);
        body.Children.Add(Spacer(16));
        foreach (var context in _service.Contexts.Where(c => c.Provider == "Claude").OrderBy(c => c.Name))
        {
            var stack = new StackPanel();
            stack.Children.Add(_ui.Text(context.Name + (context.IsDefault ? " · default" : ""), 16, true));
            stack.Children.Add(_ui.Text(context.HomePath, 12, false, _ui.Muted));
            if (ContextIdentity.HasVerifiedDuplicate(_service.Contexts.Where(c => c != context), context))
                stack.Children.Add(_ui.Text("Same provider account as another context; shown once on Overview.", 12, false, _ui.Muted));
            stack.Children.Add(_ui.Text(Freshness.DisplayState(context, DateTimeOffset.UtcNow), 12, false, _ui.Muted));
            if (context.ErrorCode is not null) stack.Children.Add(_ui.Text(Freshness.ErrorText(context.ErrorCode), 12, false, _ui.Muted));
            if (context.Snapshot is not null)
                stack.Children.Add(_ui.Text($"Last usage received {Freshness.Age(context.Snapshot.FetchedAt, DateTimeOffset.UtcNow)}", 12, false, _ui.Muted));
            else if (context.ErrorCode is null)
                stack.Children.Add(_ui.Text("Signed in. Hub checks shared plan usage automatically; use Check updates to retry now.", 12, false, _ui.Muted));
            if (context.ErrorCode == "CLAUDE_STATUSLINE_EXISTS")
                stack.Children.Add(_ui.Text("Your existing status line was preserved. Copy Hub's capture command to integrate it manually.", 12, false, _ui.Muted));
            stack.Children.Add(Spacer(10));
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(_ui.Button("Rename", () => Rename(context)));
            if (context.ErrorCode is "CLAUDE_AUTH_REQUIRED" or "CLAUDE_LOGIN_FAILED" || context.SigningIn)
            {
                var signIn = _ui.Button(context.SigningIn ? "Sign-in open in Claude Code" : "Sign in with Claude Code", () => _ = _service.SignInClaudeAsync(context), true);
                signIn.IsEnabled = !context.SigningIn;
                buttons.Children.Add(signIn);
            }
            buttons.Children.Add(_ui.Button("Check updates", () => _ = _service.RefreshClaudeContextAsync(context, true)));
            buttons.Children.Add(_ui.Button("Enable terminal capture", () =>
            {
                if (MessageBox.Show(this, "Add AI Pulse's usage capture command to this Claude context's settings.json? Existing custom status lines are preserved.", "Enable terminal capture", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                MessageBox.Show(this, ClaudeConnector.EnsureStatusLine(context) ? "Terminal capture enabled." : "Could not enable capture. An existing status line or invalid configuration was preserved.", "AI Pulse");
            }));
            buttons.Children.Add(_ui.Button("Copy capture command", () => System.Windows.Clipboard.SetText(ClaudeConnector.StatusLineCommand(context.Id))));
            buttons.Children.Add(_ui.Button("Remove from AI Pulse", () =>
            {
                if (MessageBox.Show(this, $"Remove {context.Name} from AI Pulse and remove its AI Pulse terminal capture command? Your Claude login and other settings will be kept.", "Remove context", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    _service.RemoveContext(context);
            }));
            stack.Children.Add(buttons);
            body.Children.Add(ContextManagementCard(stack));
        }
    }

    private TextBlock Glyph(string glyph, double size) => new() { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size, Foreground = _ui.Foreground, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
    private Border IconBadge(string glyph) => new() { Width = 42, Height = 44, CornerRadius = new CornerRadius(10), Background = _ui.Dark ? UiKit.Solid("#263F60") : UiKit.Solid("#E5EEFF"), Child = Glyph(glyph,22), VerticalAlignment = VerticalAlignment.Center };

    private Border ContextManagementCard(StackPanel stack)
    {
        var identity = new StackPanel { Margin = new Thickness(0,0,18,0), VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 2; i++) { var child = stack.Children[0]; stack.Children.RemoveAt(0); identity.Children.Add(child); }
        var actions = (WrapPanel)stack.Children[stack.Children.Count - 1]; stack.Children.Remove(actions);
        foreach (FrameworkElement button in actions.Children) { button.Margin = new Thickness(0,4,6,4); if (button is Button b && b.Content.ToString() == "Remove from AI Pulse") b.Foreground = UiKit.Solid("#F28E83"); }
        actions.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        row.Children.Add(identity); Grid.SetColumn(stack,1); row.Children.Add(stack); Grid.SetColumn(actions,2); row.Children.Add(actions);
        return _ui.Panel(row);
    }

    private void AddExistingContext()
    {
        using var picker = new WinForms.FolderBrowserDialog { Description = "Select an existing Codex context directory (CODEX_HOME)", UseDescriptionForTitle = true };
        if (picker.ShowDialog() != WinForms.DialogResult.OK) return;
        try { _service.AddContext(picker.SelectedPath); }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Could not add context"); }
    }

    private void AddExistingClaudeContext()
    {
        using var picker = new WinForms.FolderBrowserDialog { Description = "Select an existing Claude Code context directory (CLAUDE_CONFIG_DIR)", UseDescriptionForTitle = true };
        if (picker.ShowDialog() != WinForms.DialogResult.OK) return;
        try { _service.AddClaudeContext(picker.SelectedPath); }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Could not add context"); }
    }

    private void Rename(CodexContext context)
    {
        var dialog = new Window { Title = $"Rename {context.Provider} context", Owner = this, Width = 340, Height = 160, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = _ui.Background };
        var stack = new StackPanel { Margin = new Thickness(18) };
        var input = new TextBox { Text = context.Name, Padding = new Thickness(8), FontSize = 14 };
        stack.Children.Add(input); stack.Children.Add(Spacer(12));
        stack.Children.Add(_ui.Button("Save", () => { try { _service.RenameContext(context, input.Text); dialog.Close(); } catch (Exception e) { MessageBox.Show(dialog, e.Message); } }, true));
        dialog.Content = stack; dialog.ShowDialog();
    }

    private void BuildSettings(StackPanel body)
    {
        var s = _service.Settings;

        var general = new StackPanel();
        general.Children.Add(Check("Start AI Pulse with Windows", s.StartWithWindows, value =>
        {
            if (!StartupRegistration.Set(value))
            {
                MessageBox.Show(this, "Windows startup could not be updated. Check the logon account's Run registry key.", "AI Pulse");
                Render();
                return;
            }
            s.StartWithWindows = value;
            Save();
        }));
        general.Children.Add(Check("Start minimized to tray", s.StartMinimized, value => { s.StartMinimized = value; Save(); }));
        general.Children.Add(Check("Show notch on startup", s.ShowNotchOnStartup, value => { s.ShowNotchOnStartup = value; Save(); }));
        body.Children.Add(SettingsGroup("General", general));

        var notch = new StackPanel();
        notch.Children.Add(Check("Always on top", s.AlwaysOnTop, value => { s.AlwaysOnTop = value; Save(); }));
        notch.Children.Add(Check("Auto hide over fullscreen apps", s.AutoHideFullscreen, value => { s.AutoHideFullscreen = value; Save(); }));
        notch.Children.Add(Check("Animate expand and collapse", s.AnimateNotch, value => { s.AnimateNotch = value; Save(); }));
        notch.Children.Add(_ui.Text("Notch display", 12, true, _ui.Muted));
        var screens = WinForms.Screen.AllScreens;
        var display = new ComboBox { MinWidth = 240, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 6) };
        display.Items.Add(new ComboBoxItem { Content = "Primary display", Tag = "primary" });
        foreach (var screen in screens) display.Items.Add(new ComboBoxItem { Content = screen.DeviceName + (screen.Primary ? " · primary" : ""), Tag = screen.DeviceName });
        display.SelectedItem = display.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.DisplayId) ?? display.Items[0];
        display.SelectionChanged += (_, _) => { s.DisplayId = (string)((ComboBoxItem)display.SelectedItem).Tag; Save(); };
        notch.Children.Add(display);
        notch.Children.Add(Spacer(8));
        notch.Children.Add(_ui.Text("Auto-hide after", 12, true, _ui.Muted));
        var timeout = new ComboBox { Width = 180, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0,6,0,12) };
        foreach (var minutes in new[] { 0, 5, 10 }) timeout.Items.Add(new ComboBoxItem { Content = minutes == 0 ? "Never" : $"{minutes} minutes", Tag = minutes });
        timeout.SelectedItem = timeout.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == s.NotchAutoHideMinutes) ?? timeout.Items[1];
        timeout.SelectionChanged += (_, _) => { s.NotchAutoHideMinutes = (int)((ComboBoxItem)timeout.SelectedItem).Tag; Save(); };
        notch.Children.Add(timeout);
        notch.Children.Add(_ui.Text("Collapsed notch accounts", 12, true, _ui.Muted));
        notch.Children.Add(_ui.Text("Choose one account per provider. The notch shows 5-hour / weekly % left.", 11, false, _ui.Muted));
        foreach (var provider in new[] { "Codex", "Claude" })
        {
            notch.Children.Add(Spacer(10));
            notch.Children.Add(_ui.Text(provider == "Codex" ? "OpenAI / Codex" : "Claude", 12, false, _ui.Muted));
            var accountChoice = new ComboBox { MinWidth = 240, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 2) };
            accountChoice.Items.Add(new ComboBoxItem { Content = "Automatic", Tag = null });
            accountChoice.Items.Add(new ComboBoxItem { Content = "Hide provider", Tag = HubSettings.HiddenNotchAccount });
            var accounts = ContextIdentity.VisibleAccounts(_service.Contexts, DateTimeOffset.UtcNow)
                .Where(c => c.Provider == provider).OrderBy(c => c.Name).ToArray();
            foreach (var account in accounts)
            {
                var duplicateName = accounts.Count(c => c.Name == account.Name) > 1;
                accountChoice.Items.Add(new ComboBoxItem
                {
                    Content = duplicateName ? $"{account.Name} ({Path.GetFileName(account.HomePath)})" : account.Name,
                    Tag = ContextIdentity.AccountKey(account), ToolTip = account.HomePath
                });
            }
            var selectedKey = provider == "Codex" ? s.CodexNotchAccountKey : s.ClaudeNotchAccountKey;
            accountChoice.SelectedItem = accountChoice.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == selectedKey) ?? accountChoice.Items[0];
            accountChoice.SelectionChanged += (_, _) =>
            {
                var key = (string?)((ComboBoxItem)accountChoice.SelectedItem).Tag;
                if (provider == "Codex") s.CodexNotchAccountKey = key;
                else s.ClaudeNotchAccountKey = key;
                Save();
            };
            notch.Children.Add(accountChoice);
        }
        notch.Children.Add(Spacer(8));
        notch.Children.Add(_ui.Button("Alt, Alt   \u00B7   Show / hide notch", _toggleNotch));
        body.Children.Add(SettingsGroup("Notch", notch));

        var refresh = new StackPanel();
        refresh.Children.Add(Check("Automatic refresh", s.AutomaticRefresh, value => { s.AutomaticRefresh = value; Save(); }));
        refresh.Children.Add(_ui.Text("Refresh interval", 12, true, _ui.Muted));
        var interval = new ComboBox { Width = 150, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var value in HubSettings.RefreshIntervals)
            interval.Items.Add(new ComboBoxItem { Content = value == 1 ? "1 minute" : $"{value} minutes", Tag = value });
        interval.SelectedItem = interval.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == s.RefreshMinutes);
        interval.SelectionChanged += (_, _) => { s.RefreshMinutes = (int)((ComboBoxItem)interval.SelectedItem).Tag; Save(); };
        refresh.Children.Add(interval);
        body.Children.Add(SettingsGroup("Refresh", refresh));

        var alerts = new StackPanel();
        alerts.Children.Add(Check("Notify at 30% left", s.Alert70, value => { s.Alert70 = value; Save(); }));
        alerts.Children.Add(Check("Notify at 15% left", s.Alert85, value => { s.Alert85 = value; Save(); }));
        alerts.Children.Add(Check("Notify at 5% left", s.Alert95, value => { s.Alert95 = value; Save(); }));
        alerts.Children.Add(Check("Notify on confirmed reset", s.AlertOnReset, value => { s.AlertOnReset = value; Save(); }));
        body.Children.Add(SettingsGroup("Alerts", alerts));

        var appearance = new StackPanel();
        appearance.Children.Add(_ui.Text("Theme", 12, true, _ui.Muted));
        var theme = new ComboBox { Width = 130, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var value in new[] { "System", "Light", "Dark" }) theme.Items.Add(value);
        theme.SelectedItem = s.Theme;
        theme.SelectionChanged += (_, _) => { s.Theme = (string)theme.SelectedItem; Save(); };
        appearance.Children.Add(theme);
        body.Children.Add(SettingsGroup("Appearance", appearance));

        var advanced = new StackPanel();
        advanced.Children.Add(_ui.Text($"Providers: {_service.Contexts.Count(c => c.Provider == "Codex")} Codex · {_service.Contexts.Count(c => c.Provider == "Claude")} Claude contexts", 12, false, _ui.Muted));
        advanced.Children.Add(_ui.Text("Codex CLI: " + (_service.CodexVersion ?? "Version unavailable"), 12, false, _ui.Muted));
        advanced.Children.Add(_ui.Text("Claude Code: " + (_service.ClaudeExecutable ?? "Not found"), 12, false, _ui.Muted));
        advanced.Children.Add(_ui.Text("Executable: " + (_service.CodexExecutable ?? "Not found"), 12, false, _ui.Muted));
        advanced.Children.Add(_ui.Text("Local data: " + _service.DataDirectory, 12, false, _ui.Muted));
        advanced.Children.Add(Spacer(10));
        advanced.Children.Add(_ui.Button("Open data and logs folder", () => Process.Start(new ProcessStartInfo("explorer.exe", _service.DataDirectory) { UseShellExecute = true })));
        body.Children.Add(SettingsGroup("Advanced", advanced));
        var groups = body.Children.Cast<UIElement>().ToArray(); body.Children.Clear();
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { Margin = new Thickness(0,0,7,0) }; var right = new StackPanel { Margin = new Thickness(7,0,0,0) };
        columns.Children.Add(left); Grid.SetColumn(right,1); columns.Children.Add(right);
        foreach (var index in new[] { 0,2,4 }) left.Children.Add(groups[index]);
        foreach (var index in new[] { 1,3,5 }) right.Children.Add(groups[index]);
        body.Children.Add(columns);
        body.Children.Add(_ui.Text("Changes are saved automatically", 12, false, _ui.Muted));
        body.Children.Add(Spacer(12));
        body.Children.Add(_ui.Button("Exit AI Pulse", _exit));
    }

    private Border SettingsGroup(string title, StackPanel content)
    {
        var stack = new StackPanel(); stack.Children.Add(_ui.Text(title, 19, true));
        stack.Children.Add(_ui.Text(title switch { "General" => "App startup and basic behavior", "Notch" => "A quiet companion at the top of your screen", "Refresh" => "Keep your usage up to date", "Alerts" => "Know when you are approaching a limit", "Appearance" => "Your preferred color palette", _ => "Provider details and local data" },12,false,_ui.Muted));
        stack.Children.Add(Spacer(16)); stack.Children.Add(content);
        return _ui.Panel(stack);
    }
    private ProgressBar RemainingBar(UsageWindow window)
    {
        var bar = _ui.Progress(window.RemainingPercent ?? 0);
        bar.Foreground = window.UsedPercent >= 95 ? UiKit.Solid("#F16D6D") :
            window.UsedPercent >= 85 ? UiKit.Solid("#EBA54C") : _ui.Accent;
        bar.ToolTip = $"{UsageWindow.FormatPercent(window.RemainingPercent)} left · {window.UsedPercent:0}% used";
        return bar;
    }
    private CheckBox Check(string title, bool value, Action<bool> changed)
    {
        var control = new CheckBox { Content = title, IsChecked = value, FontSize = 13, Foreground = _ui.Foreground, Margin = new Thickness(0, 0, 0, 11), VerticalContentAlignment = VerticalAlignment.Center };
        control.Checked += (_, _) => changed(true); control.Unchecked += (_, _) => changed(false);
        return control;
    }
    private void Save() => _service.SaveSettings();
    private static Border Spacer(double height) => new() { Height = height, Background = Brushes.Transparent };
}
