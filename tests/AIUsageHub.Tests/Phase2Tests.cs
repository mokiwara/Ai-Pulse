using System.IO;
using System.Text.Json;
using AIUsageHub;
using Microsoft.Data.Sqlite;

namespace AIUsageHub.Tests;

public sealed class Phase2Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1790200000);

    [Fact]
    public void ClaudeStatusLineNormalizesOnlyReportedSubscriptionWindows()
    {
        var payload = """{"rate_limits":{"five_hour":{"used_percentage":41.5,"resets_at":$FIVE$},"seven_day":{"used_percentage":63,"resets_at":$WEEK$}},"context_window":{"remaining_percentage":9},"session_id":"do-not-store"}"""
            .Replace("$FIVE$", Now.AddHours(2).ToUnixTimeSeconds().ToString()).Replace("$WEEK$", Now.AddDays(3).ToUnixTimeSeconds().ToString());
        var snapshot = ClaudeConnector.ParseStatusLine(payload, Now)!;
        Assert.Equal(new[] { "FIVE_HOUR", "WEEKLY" }, snapshot.Windows.Select(w => w.Kind));
        Assert.Equal(41.5, snapshot.Windows[0].UsedPercent);
        Assert.Equal(58.5, snapshot.Windows[0].RemainingPercent);
        Assert.Equal(37, snapshot.Windows[1].RemainingPercent);
        Assert.Equal(Now.AddDays(3), snapshot.Windows[1].ResetsAt);
        Assert.All(snapshot.Windows, w => Assert.Null(w.Maximum));
        Assert.DoesNotContain("session_id", JsonSerializer.Serialize(snapshot));
    }

    [Fact]
    public void MissingInvalidAndExpiredClaudeFieldsDoNotBecomeZero()
    {
        Assert.Null(ClaudeConnector.ParseStatusLine("{}", Now));
        Assert.Null(ClaudeConnector.ParseStatusLine("""{"rate_limits":{"five_hour":{"used_percentage":null},"seven_day":{}}}""", Now));
        var partial = ClaudeConnector.ParseStatusLine("""{"rate_limits":{"five_hour":{"used_percentage":19,"resets_at":$OLD$},"seven_day":{"used_percentage":20}}}"""
            .Replace("$OLD$", Now.AddHours(-1).ToUnixTimeSeconds().ToString()), Now)!;
        var weekly = Assert.Single(partial.Windows);
        Assert.Equal("WEEKLY", weekly.Kind);
        Assert.Null(weekly.ResetsAt);
        Assert.Equal(80, weekly.RemainingPercent);
    }

    [Fact]
    public void DirectRemainingSourceCanBeNormalizedWithoutInversion()
    {
        var window = UsageWindow.FromRemaining("x", "WEEKLY", "Weekly", 15, Now, "direct");
        Assert.Equal(85, window.UsedPercent);
        Assert.Equal(15, window.RemainingPercent);
        Assert.Throws<ArgumentOutOfRangeException>(() => UsageWindow.FromRemaining("x", "WEEKLY", "Weekly", 110, Now, "direct"));
    }

    [Fact]
    public void ClaudeWaitingAndStaleStatesTellTheTruth()
    {
        var context = new CodexContext { Provider = "Claude" };
        Assert.Equal("Claude Code signed in · Waiting for usage data", Freshness.DisplayState(context, Now));
        context.Snapshot = ClaudeSnapshot(42, Now);
        Assert.Equal("Usage observed", Freshness.DisplayState(context, Now.AddMinutes(2)));
        Assert.Equal("Usage data stale", Freshness.DisplayState(context, Now.AddMinutes(20)));
        context.ErrorCode = "CLAUDE_AUTH_REQUIRED";
        Assert.Contains("Stale", Freshness.DisplayState(context, Now.AddMinutes(2)));
        Assert.NotNull(context.Snapshot);
    }

    [Fact]
    public void StatusLineSetupPreservesExistingConfigurationAndRemovalIsScoped()
    {
        var dir = TempFolder();
        try
        {
            var context = new CodexContext { Provider = "Claude", HomePath = dir };
            var settings = Path.Combine(dir, "settings.json");
            File.WriteAllText(settings, """{"theme":"dark","statusLine":{"type":"command","command":"my-script"}}""");
            Assert.False(ClaudeConnector.EnsureStatusLine(context));
            Assert.Contains("my-script", File.ReadAllText(settings));
            File.WriteAllText(settings, """{"theme":"dark"}""");
            Assert.True(ClaudeConnector.EnsureStatusLine(context));
            Assert.Contains("--claude-statusline", File.ReadAllText(settings));
            ClaudeConnector.RemoveStatusLine(context);
            Assert.Contains("dark", File.ReadAllText(settings));
            Assert.DoesNotContain("--claude-statusline", File.ReadAllText(settings));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ProviderAggregationAndNotchWarningsUseRemainingCapacity()
    {
        var codex = new CodexContext { Provider = "Codex", Name = "Work", Snapshot = ClaudeSnapshot(88, Now) };
        var claude = new CodexContext { Provider = "Claude", Name = "Personal", Snapshot = ClaudeSnapshot(97, Now) };
        var summary = NotchPolicy.Summary([codex, claude], Now.AddMinutes(1));
        Assert.Contains("Claude Personal", summary);
        Assert.Contains("3% left", summary);
        claude.ErrorCode = "TIMEOUT";
        Assert.Contains("Codex Work", NotchPolicy.Summary([codex, claude], Now.AddMinutes(1)));
    }

    [Fact]
    public void AlertAtFifteenLeftIsEightyFiveUsedAndDeduplicatesAcrossProviders()
    {
        var dir = TempFolder();
        try
        {
            var store = new LocalStore(dir);
            var context = new CodexContext { Provider = "Claude", Name = "Personal" };
            var settings = new HubSettings();
            Assert.Empty(AlertPolicy.Evaluate(context, ClaudeSnapshot(70, Now), ClaudeSnapshot(84, Now), settings, store, Now));
            var alerts = AlertPolicy.Evaluate(context, ClaudeSnapshot(84, Now), ClaudeSnapshot(85, Now), settings, store, Now).ToArray();
            Assert.Single(alerts);
            Assert.Contains("15%", alerts[0].Body);
            Assert.Contains("Claude", alerts[0].Title);
            Assert.Empty(AlertPolicy.Evaluate(context, ClaudeSnapshot(84, Now), ClaudeSnapshot(85, Now), settings, store, Now));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PhaseOneDatabaseMigratesWithoutLosingContextOrSettings()
    {
        var dir = TempFolder();
        try
        {
            var path = Path.Combine(dir, "hub.db");
            using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE contexts(id TEXT PRIMARY KEY,name TEXT NOT NULL,home_path TEXT NOT NULL UNIQUE,is_default INTEGER NOT NULL,identity_fingerprint TEXT,error_code TEXT,last_attempt TEXT,next_refresh TEXT,failure_count INTEGER NOT NULL DEFAULT 0);
                    CREATE TABLE snapshots(context_id TEXT PRIMARY KEY,normalized_json TEXT NOT NULL,fetched_at TEXT NOT NULL);
                    CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
                    INSERT INTO contexts(id,name,home_path,is_default,failure_count) VALUES('old','My Codex','C:\old-codex',1,0);
                    INSERT INTO settings(key,value) VALUES('hub','{"theme":"Dark","startWithWindows":true,"refreshMinutes":15}');
                    """;
                cmd.ExecuteNonQuery();
            }
            var store = new LocalStore(dir);
            var context = Assert.Single(store.LoadContexts());
            Assert.Equal("Codex", context.Provider);
            Assert.Equal("My Codex", context.Name);
            Assert.True(store.LoadSettings().StartWithWindows);
            Assert.Equal("Dark", store.LoadSettings().Theme);
            Assert.Equal(15, store.LoadSettings().RefreshMinutes);
            store.SaveContext(new CodexContext { Provider = "Claude", Name = "Claude", HomePath = Path.Combine(dir, ".claude") });
            Assert.Equal(2, store.LoadContexts().Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ClaudeCandidatesIncludeIndependentContextsWithoutDirectoryDuplicates()
    {
        var dir = TempFolder();
        try
        {
            var a = Path.Combine(dir, ".claude");
            var b = Path.Combine(dir, ".claude-work");
            Directory.CreateDirectory(a); Directory.CreateDirectory(b);
            var candidates = ClaudeConnector.CandidateHomes(dir, a);
            Assert.Equal(2, candidates.Count);
            Assert.Contains(a, candidates);
            Assert.Contains(b, candidates);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void VerifiedDuplicateIdentityIsProviderScopedAndWeakIdentityStaysSeparate()
    {
        var known = new CodexContext { Provider = "Claude", IdentityFingerprint = "verified-account" };
        Assert.True(ContextIdentity.HasVerifiedDuplicate([known], new CodexContext { Provider = "Claude", IdentityFingerprint = "verified-account" }));
        Assert.False(ContextIdentity.HasVerifiedDuplicate([known], new CodexContext { Provider = "Codex", IdentityFingerprint = "verified-account" }));
        Assert.False(ContextIdentity.HasVerifiedDuplicate([new CodexContext { Provider = "Claude", IdentityFingerprint = "context:one" }],
            new CodexContext { Provider = "Claude", IdentityFingerprint = "context:one" }));
        Assert.False(ContextIdentity.HasVerifiedDuplicate([known], new CodexContext { Provider = "Claude" }));
        var old = new CodexContext { Provider = "Claude", Name = "old", IdentityFingerprint = "verified-account", Snapshot = ClaudeSnapshot(70, Now.AddMinutes(-30)) };
        var fresh = new CodexContext { Provider = "Claude", Name = "fresh", IdentityFingerprint = "verified-account", Snapshot = ClaudeSnapshot(80, Now) };
        var otherProvider = new CodexContext { Provider = "Codex", IdentityFingerprint = "verified-account", Snapshot = ClaudeSnapshot(30, Now) };
        var visible = ContextIdentity.VisibleAccounts([old, fresh, otherProvider], Now);
        Assert.Equal(2, visible.Count);
        Assert.Contains(fresh, visible);
        Assert.Contains(otherProvider, visible);
    }

    [Fact]
    public async Task ProviderFailureDoesNotBlockAnotherProvidersPassiveUpdate()
    {
        var dir = TempFolder();
        var codexHome = Path.Combine(dir, "codex");
        var claudeHome = Path.Combine(dir, "claude");
        Directory.CreateDirectory(codexHome);
        Directory.CreateDirectory(claudeHome);
        string? feed = null;
        try
        {
            using var service = new HubService(new LocalStore(Path.Combine(dir, "store")));
            var codex = service.AddContext(codexHome, refresh: false);
            var claude = service.AddClaudeContext(claudeHome, refresh: false);
            feed = ClaudeConnector.FeedPath(claude.Id);
            var reset = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
            ClaudeConnector.CaptureStatusLine(claude.Id,
                """{"rate_limits":{"five_hour":{"used_percentage":25,"resets_at":$RESET$}}}""".Replace("$RESET$", reset.ToString()));
            Directory.Delete(codexHome);
            claude.LastAttemptAt = DateTimeOffset.UtcNow; // Authenticated fixture; no login needed.
            await Task.WhenAll(service.RefreshContextAsync(codex, true), service.RefreshClaudeContextAsync(claude, false));
            Assert.Equal("CONTEXT_MISSING", codex.ErrorCode);
            Assert.Equal(75, Assert.Single(claude.Snapshot!.Windows).RemainingPercent);
        }
        finally
        {
            if (feed is not null) File.Delete(feed);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task LiveThreeCodexAccountsWhenExplicitlyEnabled()
    {
        if (Environment.GetEnvironmentVariable("AIUSAGE_LIVE_TEST") != "1") return;
        var contexts = new LocalStore().LoadContexts().Where(c => c.Provider == "Codex")
            .GroupBy(c => c.IdentityFingerprint ?? c.Id).Select(g => g.First()).Take(3).ToArray();
        Assert.Equal(3, contexts.Length);
        var connector = new CodexConnector();
        foreach (var context in contexts)
        {
            var reading = await connector.ReadAsync(context, CancellationToken.None);
            Assert.NotEmpty(reading.Snapshot.Windows);
            Assert.All(reading.Snapshot.Windows.Where(w => w.UsedPercent is not null), w => Assert.InRange(w.UsedPercent!.Value, 0, 100));
        }
    }

    [Fact]
    public async Task LiveCodexContinuesWhenClaudeContextFailsWhenExplicitlyEnabled()
    {
        if (Environment.GetEnvironmentVariable("AIUSAGE_LIVE_TEST") != "1") return;
        var dir = TempFolder();
        try
        {
            var claudeHome = Path.Combine(dir, "claude");
            Directory.CreateDirectory(claudeHome);
            using var service = new HubService(new LocalStore(Path.Combine(dir, "hub")));
            var codex = service.AddContext(CodexDiscovery.DefaultHome, refresh: false);
            var claude = service.AddClaudeContext(claudeHome, refresh: false);
            Directory.Delete(claudeHome, true);
            await service.RefreshAllAsync(true);
            Assert.NotNull(codex.Snapshot);
            Assert.Equal("CONTEXT_MISSING", claude.ErrorCode);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static UsageSnapshot ClaudeSnapshot(double used, DateTimeOffset fetched)
        => new(fetched, "fixture", null, [new UsageWindow("weekly", "WEEKLY", "Weekly", used, null, null, null, "%", Now.AddDays(1), fetched, "fixture")]);

    private static string TempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "AIUsageHub.Phase2Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
