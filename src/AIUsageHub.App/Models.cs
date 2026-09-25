using System.Text.Json.Serialization;

namespace AIUsageHub;

public enum ConnectionState { Stale, Refreshing, Fresh, Unavailable, CodexNotFound, AuthRequired, RateLimited, Error, Unsupported }

public sealed class CodexContext
{
    public string Provider { get; set; } = "Codex";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Codex";
    public string HomePath { get; set; } = "";
    public bool IsDefault { get; set; }
    public string? IdentityFingerprint { get; set; }
    public UsageSnapshot? Snapshot { get; set; }
    public ConnectionState State { get; set; } = ConnectionState.Stale;
    public string? ErrorCode { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextRefreshAt { get; set; }
    public int FailureCount { get; set; }
    [JsonIgnore] public bool Busy { get; set; }
    [JsonIgnore] public bool SigningIn { get; set; }
}

public static class ContextIdentity
{
    public static string AccountKey(CodexContext context)
        => !string.IsNullOrWhiteSpace(context.IdentityFingerprint) &&
           !context.IdentityFingerprint.StartsWith("context:", StringComparison.Ordinal)
            ? context.IdentityFingerprint : context.Id;

    public static bool HasVerifiedDuplicate(IEnumerable<CodexContext> contexts, CodexContext candidate)
        => !string.IsNullOrWhiteSpace(candidate.IdentityFingerprint) &&
           !candidate.IdentityFingerprint.StartsWith("context:", StringComparison.Ordinal) &&
           contexts.Any(c => c.Provider == candidate.Provider && c.IdentityFingerprint == candidate.IdentityFingerprint);

    public static IReadOnlyList<CodexContext> VisibleAccounts(IEnumerable<CodexContext> contexts, DateTimeOffset now)
        => contexts.GroupBy(c => c.Provider + ":" + AccountKey(c))
            .Select(group => group.OrderByDescending(c => Freshness.IsFresh(c, now))
                .ThenByDescending(c => c.Snapshot?.FetchedAt ?? DateTimeOffset.MinValue)
                .ThenByDescending(c => c.IsDefault).First()).ToArray();
}

public sealed record UsageWindow(
    string Id, string Kind, string Title, double? UsedPercent,
    decimal? Used, decimal? Remaining, decimal? Maximum, string Unit,
    DateTimeOffset? ResetsAt, DateTimeOffset FetchedAt, string Source,
    bool IsEstimated = false)
{
    // Every provider stores the percentage consumed. Remaining is always derived from it.
    // Codex usedPercent and Claude status-line used_percentage both mean consumed capacity.
    [JsonIgnore] public double? RemainingPercent => UsedPercent is { } used && double.IsFinite(used) && used is >= 0 and <= 100 ? 100 - used : null;

    public static string FormatPercent(double? remaining) => remaining is null ? "—" : remaining is > 0 and < 1 ? "<1%" : $"{remaining:0}%";

    public static UsageWindow FromRemaining(string id, string kind, string title, double percentRemaining,
        DateTimeOffset fetchedAt, string source, DateTimeOffset? resetsAt = null)
    {
        if (!double.IsFinite(percentRemaining) || percentRemaining is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percentRemaining));
        return new UsageWindow(id, kind, title, 100 - percentRemaining, null, null, null, "%", resetsAt, fetchedAt, source);
    }
}

public sealed record UsageSnapshot(DateTimeOffset FetchedAt, string Source, string? Plan,
    IReadOnlyList<UsageWindow> Windows);

public sealed class HubSettings
{
    public static IReadOnlyList<int> RefreshIntervals { get; } = Array.AsReadOnly([1, 2, 5, 10, 15]);
    public const string HiddenNotchAccount = "hidden";
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool ShowNotchOnStartup { get; set; } = true;
    public bool ShowNotch { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public bool AutoHideFullscreen { get; set; } = true;
    public int NotchAutoHideMinutes { get; set; } = 5;
    public bool AnimateNotch { get; set; } = true;
    public bool AutomaticRefresh { get; set; } = true;
    public int RefreshMinutes { get; set; } = 2;
    public string? CodexNotchAccountKey { get; set; }
    public string? ClaudeNotchAccountKey { get; set; }
    public string DisplayId { get; set; } = "primary";
    public string Theme { get; set; } = "System";
    public bool Alert70 { get; set; } = true;
    public bool Alert85 { get; set; } = true;
    public bool Alert95 { get; set; } = true;
    public bool AlertOnReset { get; set; } = true;
    public void Normalize()
    {
        RefreshMinutes = RefreshIntervals.MinBy(value => Math.Abs((long)value - RefreshMinutes));
        if (NotchAutoHideMinutes is not (0 or 5 or 10)) NotchAutoHideMinutes = 5;
        if (Theme is not ("System" or "Dark" or "Light")) Theme = "System";
        if (string.IsNullOrWhiteSpace(DisplayId)) DisplayId = "primary";
    }

}

public static class Freshness
{
    public static bool IsFresh(CodexContext context, DateTimeOffset now)
    {
        if (context.Snapshot is null || context.Snapshot.Windows.Count == 0 || context.ErrorCode is not null) return false;
        if (now - context.Snapshot.FetchedAt > TimeSpan.FromMinutes(15)) return false;
        return context.Snapshot.Windows.All(w => w.ResetsAt is null || w.ResetsAt > now);
    }

    public static string DisplayState(CodexContext context, DateTimeOffset now)
    {
        if (context.SigningIn) return "Sign-in open in Claude Code";
        if (context.Busy) return "Refreshing";
        if (context.ErrorCode is not null)
            return context.Snapshot is null ? ShortError(context.ErrorCode) : $"Stale · {ShortError(context.ErrorCode)}";
        if (context.Snapshot is null) return context.Provider == "Claude" ? "Claude Code signed in · Waiting for usage data" : "No usage data yet";
        if (context.Provider == "Claude") return IsFresh(context, now) ? "Usage observed" : "Usage data stale";
        return IsFresh(context, now) ? "Fresh" : "Stale";
    }

    private static string ShortError(string code) => code switch
    {
        "CLAUDE_AUTH_REQUIRED" => "Not signed in",
        "CLAUDE_NOT_FOUND" => "Claude Code not installed",
        "CLAUDE_STATUSLINE_EXISTS" => "Usage feed needs setup",
        "CLAUDE_UNSUPPORTED" => "Unsupported Claude version",
        "CLAUDE_API_AUTH" => "API billing · no subscription quota",
        "CODEX_NOT_FOUND" => "Codex CLI not found",
        "CODEX_LAUNCH_FAILED" => "Could not start Codex CLI",
        "AUTH_REQUIRED" => "Authentication required in Codex",
        "IDENTITY_CHANGED" => "Account changed",
        _ => "Connector error"
    };

    public static string ErrorText(string code) => code switch
    {
        "CODEX_NOT_FOUND" => "Codex CLI was not found.",
        "CODEX_LAUNCH_FAILED" => "AI Pulse found Codex CLI but could not start it. It will retry automatically; you can also refresh now.",
        "AUTH_REQUIRED" => "Authenticate this context using Codex CLI.",
        "RATE_LIMITED" => "Codex rate limited the read.",
        "UNSUPPORTED" => "This Codex version returned an unsupported usage format.",
        "TIMEOUT" => "The provider CLI did not respond in time.",
        "IDENTITY_CHANGED" => "Provider account changed. Remove and re-add this context to monitor the new account.",
        "CONTEXT_MISSING" => "Context directory is unavailable.",
        "CLAUDE_NOT_FOUND" => "Claude Code not installed.",
        "CLAUDE_AUTH_REQUIRED" => "Claude Code is installed but not signed in.",
        "CLAUDE_LOGIN_FAILED" => "Could not start Claude Code sign-in. Check that Claude Code runs in your terminal.",
        "CLAUDE_STATUSLINE_EXISTS" => "Existing Claude status line is preserved. See Accounts for setup.",
        "CLAUDE_UNSUPPORTED" => "Claude Code 2.1.251 or newer is required for usage data.",
        "CLAUDE_API_AUTH" => "Claude Code is using API billing; subscription limits are unavailable.",
        _ => "Unable to read provider usage."
    };

    public static string Age(DateTimeOffset? value, DateTimeOffset now)
    {
        if (value is null) return "Never";
        var span = now - value.Value;
        if (span < TimeSpan.FromMinutes(1)) return "Just now";
        if (span < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)span.TotalMinutes)}m ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours}h ago";
        return value.Value.ToLocalTime().ToString("g");
    }
}
