using System.Net.NetworkInformation;
using System.Text.Json;

namespace AIUsageHub;

public sealed record HubNotice(string Title, string Body);

public sealed class HubService : IDisposable
{
    private readonly LocalStore _store;
    private readonly CodexConnector _codex = new();
    private readonly ClaudeConnector _claude = new();
    private readonly AsyncLifetime _lifetime = new();
    private CancellationToken StopToken => _lifetime.Token;
    private bool _disposed;
    private bool _discoveringClaude;
    public Task Stopped => _lifetime.Completion;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Diagnostics.Process> _claudeLogins = new();
    private bool _discovering;
    private Task? _scheduler;
    private int _savedRefreshMinutes;
    public List<CodexContext> Contexts { get; }
    public HubSettings Settings { get; }
    public string DataDirectory => _store.DirectoryPath;
    public string? CodexExecutable => _codex.ExecutablePath;
    public string? ClaudeExecutable => _claude.ExecutablePath;
    public string? CodexVersion { get; private set; }
    public string DiscoveryStatus { get; private set; } = "";
    public event Action? Changed;
    public event Action<HubNotice>? Notice;

    public HubService(LocalStore store)
    {
        _store = store;
        Settings = store.LoadSettings();
        _savedRefreshMinutes = Settings.RefreshMinutes;
        Contexts = store.LoadContexts();
        NetworkChange.NetworkAvailabilityChanged += NetworkChanged;
        _store.PruneAlerts(DateTimeOffset.UtcNow);
        SafeLog.Prune(_store.DirectoryPath);
    }

    public void Start()
    {
        if (_disposed || _scheduler is not null) return;
        _scheduler = SchedulerAsync(StopToken);
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        using var operation = _lifetime.TryEnter();
        if (operation is null) return;
        try
        {
            CodexVersion = await CodexDiscovery.ReadVersionAsync(StopToken);
            if (StopToken.IsCancellationRequested) return;
            Changed?.Invoke();
            await RefreshAllAsync(true);
            await Task.WhenAll(DiscoverExistingContextsAsync(), DiscoverClaudeContextsAsync());
        }
        catch (OperationCanceledException) when (StopToken.IsCancellationRequested) { }
        catch { SafeLog.Write(_store.DirectoryPath, "PROVIDER_ERROR", "initialize"); }
    }

    public async Task DiscoverExistingContextsAsync()
    {
        using var operation = _lifetime.TryEnter();
        if (operation is null) return;
        if (_discovering) return;
        _discovering = true;
        DiscoveryStatus = "Scanning local Codex contexts…";
        Changed?.Invoke();
        try
        {
            if (_codex.ExecutablePath is null) { DiscoveryStatus = "Codex CLI was not found."; return; }
            foreach (var path in CodexDiscovery.CandidateHomes())
            {
                if (StopToken.IsCancellationRequested) return;
                if (Contexts.Any(c => c.Provider == "Codex" && string.Equals(c.HomePath, path, StringComparison.OrdinalIgnoreCase))) continue;
                if (_store.IsIgnoredHome(path)) continue;
                var candidate = new CodexContext { HomePath = path };
                try
                {
                    var reading = await _codex.ReadAsync(candidate, StopToken);
                    if (StopToken.IsCancellationRequested) return;
                    if (_store.IsIgnoredHome(path) || Contexts.Any(c => c.Provider == "Codex" && string.Equals(c.HomePath, path, StringComparison.OrdinalIgnoreCase))) continue;
                    candidate.IdentityFingerprint = reading.IdentityFingerprint;
                    if (ContextIdentity.HasVerifiedDuplicate(Contexts, candidate)) continue;
                    var directory = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                    candidate.Name = directory.Equals(".codex", StringComparison.OrdinalIgnoreCase) ? "Codex Desktop" : directory.TrimStart('.');
                    candidate.IdentityFingerprint = reading.IdentityFingerprint;
                    candidate.Snapshot = reading.Snapshot;
                    candidate.State = ConnectionState.Fresh;
                    candidate.LastAttemptAt = reading.Snapshot.FetchedAt;
                    candidate.NextRefreshAt = RefreshPolicy.NextSuccess(candidate, reading.Snapshot, Settings);
                    Contexts.Add(candidate);
                    _store.SaveContext(candidate);
                    _store.SaveSnapshot(candidate);
                    foreach (var notice in AlertPolicy.Evaluate(candidate, null, reading.Snapshot, Settings, _store, DateTimeOffset.UtcNow))
                        Notice?.Invoke(notice);
                    Changed?.Invoke();
                }
                catch (OperationCanceledException) when (StopToken.IsCancellationRequested) { return; }
                catch (Exception) { /* Only verified contexts are auto-registered. */ }
            }
            DiscoveryStatus = $"Detected {Contexts.Where(c => c.Provider == "Codex").Select(c => c.IdentityFingerprint ?? c.Id).Distinct().Count()} Codex account(s).";
        }
        finally { _discovering = false; Changed?.Invoke(); }
    }

    public CodexContext? DetectDefault()
    {
        var path = CodexDiscovery.DefaultHome;
        return Directory.Exists(path) ? AddContext(path, "Personal", true) : null;
    }

    public CodexContext? DetectDefaultClaude()
    {
        var path = ClaudeConnector.DefaultHome;
        return Directory.Exists(path) ? AddClaudeContext(path, "Claude", true) : null;
    }

    public CodexContext AddClaudeContext(string path, string? name = null, bool isDefault = false, bool refresh = true)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        if (!Directory.Exists(canonical)) throw new DirectoryNotFoundException("The selected Claude context directory does not exist.");
        var existing = Contexts.FirstOrDefault(c => c.Provider == "Claude" && string.Equals(c.HomePath, canonical, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        _store.UnignoreHome(canonical);
        var context = new CodexContext { Provider = "Claude", Name = name ?? new DirectoryInfo(canonical).Name,
            HomePath = canonical, IsDefault = isDefault };
        Contexts.Add(context);
        _store.SaveContext(context);
        Changed?.Invoke();
        if (refresh) _ = RefreshClaudeContextAsync(context, true);
        return context;
    }

    public async Task DiscoverClaudeContextsAsync()
    {
        using var operation = _lifetime.TryEnter();
        if (operation is null) return;
        if (_discoveringClaude) return;
        _discoveringClaude = true;
        try
        {
        foreach (var path in ClaudeConnector.CandidateHomes())
        {
            if (StopToken.IsCancellationRequested) return;
            if (Contexts.Any(c => c.Provider == "Claude" && string.Equals(c.HomePath, path, StringComparison.OrdinalIgnoreCase))) continue;
            if (_store.IsIgnoredHome(path)) continue;
            var candidate = new CodexContext { Provider = "Claude", HomePath = path };
            var defaultHome = string.Equals(path, ClaudeConnector.DefaultHome, StringComparison.OrdinalIgnoreCase);
            try
            {
                var probe = await _claude.ProbeAsync(candidate, StopToken);
                if (StopToken.IsCancellationRequested) return;
                if (_store.IsIgnoredHome(path) || Contexts.Any(c => c.Provider == "Claude" && string.Equals(c.HomePath, path, StringComparison.OrdinalIgnoreCase))) continue;
                if (!defaultHome && probe.Error is not null) continue;
                candidate.IdentityFingerprint = probe.Identity;
                if (ContextIdentity.HasVerifiedDuplicate(Contexts, candidate)) continue;
                var context = AddClaudeContext(path, defaultHome ? "Personal" : new DirectoryInfo(path).Name.TrimStart('.'), defaultHome, false);
                context.IdentityFingerprint = probe.Identity;
                context.ErrorCode = probe.Error;
                context.State = probe.Error == "CLAUDE_AUTH_REQUIRED" ? ConnectionState.AuthRequired : ConnectionState.Stale;
                _store.SaveContext(context);
                await RefreshClaudeContextAsync(context, true);
            }
            catch (OperationCanceledException) when (StopToken.IsCancellationRequested) { return; }
            catch { /* Only known local contexts are registered. */ }
        }
        }
        finally { _discoveringClaude = false; }
    }

    public CodexContext AddContext(string path, string? name = null, bool isDefault = false, bool refresh = true)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        if (!Directory.Exists(canonical)) throw new DirectoryNotFoundException("The selected Codex context directory does not exist.");
        var existing = Contexts.FirstOrDefault(c => c.Provider == "Codex" && string.Equals(c.HomePath, canonical, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        _store.UnignoreHome(canonical);
        var context = new CodexContext { Name = name ?? new DirectoryInfo(canonical).Name, HomePath = canonical, IsDefault = isDefault };
        Contexts.Add(context);
        _store.SaveContext(context);
        Changed?.Invoke();
        if (refresh) _ = RefreshContextAsync(context, true);
        return context;
    }

    public void RenameContext(CodexContext context, string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 50) throw new ArgumentException("Use a name between 1 and 50 characters.");
        context.Name = name;
        _store.SaveContext(context);
        Changed?.Invoke();
    }

    public void RemoveContext(CodexContext context)
    {
        var accountKey = ContextIdentity.AccountKey(context);
        if (_claudeLogins.TryGetValue(context.Id, out var login))
            try { if (!login.HasExited) login.Kill(true); } catch { }
        if (context.Provider == "Claude") ClaudeConnector.RemoveStatusLine(context);
        Contexts.Remove(context);
        _store.RemoveContext(context.Id, context.HomePath);
        if (!Contexts.Any(c => c.Provider == context.Provider && ContextIdentity.AccountKey(c) == accountKey))
        {
            if (context.Provider == "Codex" && Settings.CodexNotchAccountKey == accountKey) Settings.CodexNotchAccountKey = null;
            if (context.Provider == "Claude" && Settings.ClaudeNotchAccountKey == accountKey) Settings.ClaudeNotchAccountKey = null;
            _store.SaveSettings(Settings);
        }
        if (context.Provider == "Claude") { try { File.Delete(ClaudeConnector.FeedPath(context.Id)); } catch { } }
        Changed?.Invoke();
    }

    public bool DefaultHomeWasRemoved() => _store.IsIgnoredHome(CodexDiscovery.DefaultHome);
    public bool DefaultClaudeHomeWasRemoved() => _store.IsIgnoredHome(ClaudeConnector.DefaultHome);

    public void SaveSettings()
    {
        Settings.Normalize();
        if (Settings.RefreshMinutes != _savedRefreshMinutes)
        {
            foreach (var context in Contexts.Where(c => c.Snapshot is not null && c.ErrorCode is null))
            {
                context.NextRefreshAt = RefreshPolicy.NextSuccess(context, context.Snapshot!, Settings);
                _store.SaveContext(context);
            }
            _savedRefreshMinutes = Settings.RefreshMinutes;
        }
        _store.SaveSettings(Settings);
        Changed?.Invoke();
    }

    public async Task RefreshAllAsync(bool manual)
    {
        using var operation = _lifetime.TryEnter();
        if (operation is null) return;
        await Task.WhenAll(Contexts.ToArray().Select(c => RefreshContextAsync(c, manual)));
    }

    public async Task RefreshOldOnOpenAsync()
    {
        using var operation = _lifetime.TryEnter();
        if (operation is null) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var context in Contexts.ToArray())
        {
            if (context.Snapshot is not null && now - context.Snapshot.FetchedAt < TimeSpan.FromMinutes(2)) continue;
            if (context.LastAttemptAt is not null && now - context.LastAttemptAt < TimeSpan.FromMinutes(2)) continue;
            if (context.ErrorCode is "RATE_LIMITED" && context.NextRefreshAt > now) continue;
            await RefreshContextAsync(context, true);
        }
    }

    public async Task RefreshContextAsync(CodexContext context, bool manual)
    {
        using var operation = _lifetime.TryEnter();
        if (operation is null) return;
        if (context.Provider == "Claude") { await RefreshClaudeContextAsync(context, manual); return; }
        if (!Contexts.Contains(context) || context.Busy) return;
        if ((!manual || context.ErrorCode is "RATE_LIMITED") && context.NextRefreshAt > DateTimeOffset.UtcNow) return;
        context.Busy = true;
        try { await _refreshLock.WaitAsync(StopToken); }
        catch (OperationCanceledException) { context.Busy = false; return; }
        try
        {
            if (StopToken.IsCancellationRequested || !Contexts.Contains(context)) return;
            context.Busy = true;
            context.State = ConnectionState.Refreshing;
            context.LastAttemptAt = DateTimeOffset.UtcNow;
            Changed?.Invoke();
            var previous = context.Snapshot;
            try
            {
                var reading = await _codex.ReadAsync(context, StopToken);
                if (StopToken.IsCancellationRequested || !Contexts.Contains(context)) return;
                if (context.IdentityFingerprint is not null && !context.IdentityFingerprint.StartsWith("context:") &&
                    !reading.IdentityFingerprint.StartsWith("context:") && context.IdentityFingerprint != reading.IdentityFingerprint)
                    throw new CodexException("IDENTITY_CHANGED");
                context.IdentityFingerprint = reading.IdentityFingerprint;
                context.Snapshot = reading.Snapshot;
                context.ErrorCode = null;
                context.FailureCount = 0;
                context.State = ConnectionState.Fresh;
                context.NextRefreshAt = RefreshPolicy.NextSuccess(context, reading.Snapshot, Settings);
                _store.SaveContext(context);
                _store.SaveSnapshot(context);
                foreach (var notice in AlertPolicy.Evaluate(context, previous, reading.Snapshot, Settings, _store, DateTimeOffset.UtcNow))
                    Notice?.Invoke(notice);
            }
            catch (OperationCanceledException) when (StopToken.IsCancellationRequested) { return; }
            catch (CodexException error) { if (Contexts.Contains(context)) RecordFailure(context, error.Code, error.Stage); }
            catch (Exception) { if (!StopToken.IsCancellationRequested && Contexts.Contains(context)) RecordFailure(context, "PROVIDER_ERROR"); }
            finally { context.Busy = false; Changed?.Invoke(); }
        }
        finally { context.Busy = false; _refreshLock.Release(); }
    }

    public async Task SignInClaudeAsync(CodexContext context)
    {
        using var operation = _lifetime.TryEnter();
        if (operation is null) return;
        if (context.Provider != "Claude" || context.SigningIn || !Contexts.Contains(context)) return;
        var exe = _claude.ExecutablePath;
        if (exe is null) { context.ErrorCode = "CLAUDE_NOT_FOUND"; Changed?.Invoke(); return; }
        context.SigningIn = true;
        Changed?.Invoke();
        using var process = new System.Diagnostics.Process
        { StartInfo = ClaudeConnector.LoginStartInfo(exe, context.HomePath) };
        try
        {
            process.Start();
            _claudeLogins[context.Id] = process;
            // Bounded checks only while the official login is open. No model requests.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(StopToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            while (!process.HasExited)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), timeout.Token);
                if (!Contexts.Contains(context)) break;
                await RefreshClaudeContextAsync(context, true);
                if (context.ErrorCode is null) break;
            }
            if (Contexts.Contains(context)) await RefreshClaudeContextAsync(context, true);
        }
        catch (OperationCanceledException) { }
        catch { context.ErrorCode = "CLAUDE_LOGIN_FAILED"; }
        finally
        {
            _claudeLogins.TryRemove(context.Id, out _);
            try { if (!process.HasExited) process.Kill(true); } catch { }
            context.SigningIn = false;
            Changed?.Invoke();
        }
    }

    public async Task RefreshClaudeContextAsync(CodexContext context, bool manual)
    {
        using var operation = _lifetime.TryEnter();
        if (operation is null) return;
        if (!Contexts.Contains(context) || context.Busy) return;
        var now = DateTimeOffset.UtcNow;
        // Passive feed checks are cheap; auth probes are bounded and happen only on manual checks
        // or after a longer interval, never to manufacture usage.
        if (!manual && context.NextRefreshAt > now) return;
        context.Busy = true;
        try
        {
            var previous = context.Snapshot;
            if (manual || context.LastAttemptAt is null || now - context.LastAttemptAt > TimeSpan.FromMinutes(context.ErrorCode == "CLAUDE_AUTH_REQUIRED" ? 1 : 10))
            {
                var probe = await _claude.ProbeAsync(context, StopToken);
                if (StopToken.IsCancellationRequested || !Contexts.Contains(context)) return;
                context.LastAttemptAt = now;
                if (probe.Identity is not null && context.IdentityFingerprint is not null && context.IdentityFingerprint != probe.Identity)
                {
                    context.ErrorCode = "IDENTITY_CHANGED";
                    context.State = ConnectionState.Stale;
                    _store.SaveContext(context);
                    return;
                }
                context.IdentityFingerprint = probe.Identity ?? context.IdentityFingerprint;
                if (probe.Plan is { Length: > 0 } && context.Snapshot is not null && context.Snapshot.Plan != probe.Plan)
                {
                    context.Snapshot = context.Snapshot with { Plan = probe.Plan };
                    _store.SaveSnapshot(context);
                }
                if (probe.Error is not null) context.ErrorCode = probe.Error;
                else context.ErrorCode = null;

            }
            var path = ClaudeConnector.FeedPath(context.Id);
            if ((context.ErrorCode is null or "CLAUDE_STATUSLINE_EXISTS") && File.Exists(path))
            {
                UsageSnapshot? feed = null;
                try { feed = JsonSerializer.Deserialize<UsageSnapshot>(await BoundedText.ReadFileAsync(path, 100_000, StopToken)); }
                catch (JsonException) { }
                catch (IOException) { }
                if (StopToken.IsCancellationRequested || !Contexts.Contains(context)) return;
                if (feed is not null && feed.FetchedAt > (previous?.FetchedAt ?? DateTimeOffset.MinValue) &&
                    feed.Windows is { Count: > 0 and <= 10 } && feed.FetchedAt <= now.AddSeconds(5) &&
                    feed.Windows.All(w => w.Source == "Claude Code status line" && w.UsedPercent is >= 0 and <= 100))
                {
                    context.Snapshot = feed;
                    context.ErrorCode = null;
                    _store.SaveSnapshot(context);
                    foreach (var notice in AlertPolicy.Evaluate(context, previous, feed, Settings, _store, now)) Notice?.Invoke(notice);
                }
            }
            if (context.ErrorCode is null or "CLAUDE_STATUSLINE_EXISTS")
            {
                var usage = await _claude.ReadUsageAsync(context, StopToken);
                if (StopToken.IsCancellationRequested || !Contexts.Contains(context)) return;
                if (usage is not null && usage.FetchedAt > (context.Snapshot?.FetchedAt ?? DateTimeOffset.MinValue))
                {
                    var beforeUsage = context.Snapshot;
                    context.Snapshot = usage with { Plan = beforeUsage?.Plan };
                    context.ErrorCode = null;
                    _store.SaveSnapshot(context);
                    foreach (var notice in AlertPolicy.Evaluate(context, beforeUsage, context.Snapshot, Settings, _store, DateTimeOffset.UtcNow))
                        Notice?.Invoke(notice);
                }
            }
            context.State = context.Snapshot is null ? context.ErrorCode == "CLAUDE_AUTH_REQUIRED" ? ConnectionState.AuthRequired : ConnectionState.Unavailable
                : Freshness.IsFresh(context, now) ? ConnectionState.Fresh : ConnectionState.Stale;
            context.NextRefreshAt = DateTimeOffset.UtcNow.Add(context.ErrorCode == "CLAUDE_AUTH_REQUIRED"
                ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(Settings.RefreshMinutes));
            _store.SaveContext(context);
        }
        catch (OperationCanceledException) when (StopToken.IsCancellationRequested) { }
        catch { context.ErrorCode = "PROVIDER_ERROR"; context.State = context.Snapshot is null ? ConnectionState.Error : ConnectionState.Stale; }
        finally { context.Busy = false; Changed?.Invoke(); }
    }

    private void RecordFailure(CodexContext context, string code, string? detail = null)
    {
        context.ErrorCode = code;
        context.State = code switch
        {
            "CODEX_NOT_FOUND" => ConnectionState.CodexNotFound,
            "AUTH_REQUIRED" => ConnectionState.AuthRequired,
            "RATE_LIMITED" => ConnectionState.RateLimited,
            "UNSUPPORTED" => ConnectionState.Unsupported,
            _ => context.Snapshot is null ? ConnectionState.Error : ConnectionState.Stale
        };
        context.FailureCount = Math.Min(context.FailureCount + 1, 6);
        context.NextRefreshAt = RefreshPolicy.NextFailure(DateTimeOffset.UtcNow, context.FailureCount, code);
        _store.SaveContext(context);
        SafeLog.Write(_store.DirectoryPath, code, context.Id, detail);
    }

    private async Task SchedulerAsync(CancellationToken token)
    {
        using var operation = _lifetime.TryEnter();
        if (operation is null) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        var ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                if (!Settings.AutomaticRefresh) continue;
                foreach (var context in Contexts.ToArray())
                    if (context.NextRefreshAt is null || context.NextRefreshAt <= DateTimeOffset.UtcNow)
                    {
                        try { await RefreshContextAsync(context, false); }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                        catch { SafeLog.Write(_store.DirectoryPath, "PROVIDER_ERROR", "scheduler"); }
                    }
                if (++ticks % 3 == 0) Changed?.Invoke(); // freshness and reset labels can change without a new result
            }
        }
        catch (OperationCanceledException) { }
    }

    private void NetworkChanged(object? sender, NetworkAvailabilityEventArgs args)
    {
        if (!_disposed && args.IsAvailable && Settings.AutomaticRefresh)
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => _ = RefreshAllAsync(true));
    }

    public void Resume()
    {
        if (_disposed) return;
        if (Settings.AutomaticRefresh) _ = RefreshAllAsync(true);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NetworkChange.NetworkAvailabilityChanged -= NetworkChanged;
        Changed = null;
        Notice = null;
        _lifetime.Dispose();
        foreach (var process in _claudeLogins.Values)
            try { if (!process.HasExited) process.Kill(true); } catch { }
        _ = Stopped.ContinueWith(_ => _refreshLock.Dispose(), TaskScheduler.Default);
    }

}

public static class RefreshPolicy
{
    public static DateTimeOffset NextSuccess(CodexContext context, UsageSnapshot snapshot, HubSettings settings)
    {
        var next = snapshot.FetchedAt.AddMinutes(settings.RefreshMinutes);
        var soonestReset = snapshot.Windows.Where(w => w.ResetsAt > snapshot.FetchedAt).Select(w => w.ResetsAt).Min();
        if (soonestReset is not null && soonestReset < next) next = soonestReset.Value.AddSeconds(30);
        var jitter = (uint)context.Id.GetHashCode() % 10;
        return next.AddSeconds(jitter);
    }

    public static DateTimeOffset NextFailure(DateTimeOffset now, int failures, string? code = null)
        => now.AddMinutes(Math.Min(code is "CODEX_NOT_FOUND" or "CODEX_LAUNCH_FAILED" ? 5 : 30,
            Math.Pow(2, Math.Clamp(failures, 1, 6) - 1)));
}

public static class AlertPolicy
{
    public static IEnumerable<HubNotice> Evaluate(CodexContext context, UsageSnapshot? previous, UsageSnapshot current,
        HubSettings settings, LocalStore store, DateTimeOffset now)
    {
        foreach (var window in current.Windows)
        {
            var old = previous?.Windows.FirstOrDefault(w => w.Id == window.Id);
            if (settings.AlertOnReset && old?.ResetsAt is not null && window.ResetsAt is not null &&
                window.ResetsAt > old.ResetsAt && (old.ResetsAt <= now || (old.UsedPercent ?? 0) > (window.UsedPercent ?? 0) + 10))
            {
                var resetKey = $"{context.IdentityFingerprint ?? context.Id}:{window.Id}:reset:{window.ResetsAt.Value.ToUnixTimeSeconds()}";
                if (store.RecordAlert(resetKey, now))
                    yield return new HubNotice($"{(context.Provider == "Claude" ? "Claude" : "OpenAI")} · {context.Name}", $"{window.Title} usage window reset.");
            }
            if (window.UsedPercent is null || window.ResetsAt is not null && window.ResetsAt <= now) continue;
            var best = 0;
            foreach (var (threshold, enabled) in new[] { (70, settings.Alert70), (85, settings.Alert85), (95, settings.Alert95) })
            {
                if (window.ResetsAt is null && old?.UsedPercent >= threshold && window.UsedPercent <= threshold - 5)
                    store.DeleteAlert($"{context.IdentityFingerprint ?? context.Id}:{window.Id}:threshold:{threshold}:unknown");
                if (!enabled || window.UsedPercent < threshold || old?.UsedPercent >= threshold) continue;
                var key = $"{context.IdentityFingerprint ?? context.Id}:{window.Id}:threshold:{threshold}:{window.ResetsAt?.ToUnixTimeSeconds().ToString() ?? "unknown"}";
                if (store.RecordAlert(key, now)) best = Math.Max(best, threshold);
            }
            if (best > 0)
                yield return new HubNotice($"{(context.Provider == "Claude" ? "Claude" : "OpenAI")} · {context.Name}", $"Only {window.RemainingPercent:0}% of your {window.Title.ToLowerInvariant()} usage remains (crossed {best}% used alert).");
        }
    }
}

public static class SafeLog
{
    public static void Prune(string directory)
    {
        try
        {
            foreach (var name in new[] { "hub.log", "hub.previous.log" })
            {
                var file = Path.Combine(directory, name);
                if (File.Exists(file) && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14)) File.Delete(file);
            }
        }
        catch { }
    }

    public static void Write(string directory, string code, string contextId, string? detail = null)
    {
        try
        {
            var allowed = new HashSet<string> { "CODEX_NOT_FOUND", "CODEX_LAUNCH_FAILED", "AUTH_REQUIRED", "RATE_LIMITED", "UNSUPPORTED", "TIMEOUT", "PROVIDER_ERROR", "IDENTITY_CHANGED", "CONTEXT_MISSING" };
            if (!allowed.Contains(code)) code = "PROVIDER_ERROR";
            var file = Path.Combine(directory, "hub.log");
            if (File.Exists(file) && new FileInfo(file).Length > 1_000_000) File.Move(file, Path.Combine(directory, "hub.previous.log"), true);
            // Only the launch exception type and numeric error code are recorded. Never log
            // exception messages, paths, account identifiers, or provider output.
            var safeDetail = code == "CODEX_LAUNCH_FAILED" && detail is not null &&
                System.Text.RegularExpressions.Regex.IsMatch(detail, @"^(?:win32|[A-Za-z]+):[0-9A-F]{8}$")
                ? $" detail={detail}" : "";
            File.AppendAllText(file, $"{DateTimeOffset.UtcNow:O} {code} context={contextId}{safeDetail}\n");
        }
        catch { /* diagnostics must never crash the utility */ }
    }
}
