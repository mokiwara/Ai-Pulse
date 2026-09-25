using System.IO;
using System.Text.Json;
using AIUsageHub;

namespace AIUsageHub.Tests;

public sealed class Phase1Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1790200000);

    private static JsonElement Fixture()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rate-limits.json")));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void MultiBucketNormalizationDoesNotDuplicateLegacyOrInventCredits()
    {
        var snapshot = CodexNormalizer.ParseRateLimits(Fixture(), Now);
        Assert.Equal(3, snapshot.Windows.Count);
        Assert.Equal(new[] { "FIVE_HOUR", "WEEKLY", "DAILY" }, snapshot.Windows.Select(w => w.Kind));
        Assert.DoesNotContain(snapshot.Windows, w => w.Kind == "CREDITS");
        Assert.Equal(48, snapshot.Windows[1].UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790773280), snapshot.Windows[1].ResetsAt);
        Assert.Null(snapshot.Windows[1].Maximum);
    }

    [Fact]
    public void WeeklyOnlyDoesNotManufactureFiveHourWindow()
    {
        using var doc = JsonDocument.Parse("""{"rateLimits":{"secondary":{"usedPercent":42,"windowDurationMins":10080}}}""");
        var snapshot = CodexNormalizer.ParseRateLimits(doc.RootElement, Now);
        var window = Assert.Single(snapshot.Windows);
        Assert.Equal("WEEKLY", window.Kind);
        Assert.Null(window.ResetsAt);
    }

    [Fact]
    public void WindowLabelsFollowDurationAndDisplayMatchesCodexLeftPercent()
    {
        using var doc = JsonDocument.Parse("""{"rateLimits":{"primary":{"usedPercent":19,"windowDurationMins":10080},"secondary":{"usedPercent":48,"windowDurationMins":300}}}""");
        var snapshot = CodexNormalizer.ParseRateLimits(doc.RootElement, Now);
        Assert.Equal("WEEKLY", snapshot.Windows[0].Kind);
        Assert.Equal(81, snapshot.Windows[0].RemainingPercent);
        Assert.Equal("FIVE_HOUR", snapshot.Windows[1].Kind);
        Assert.Equal(52, snapshot.Windows[1].RemainingPercent);
    }

    [Fact]
    public void UnsupportedResultFailsWithoutFakeZero()
    {
        using var doc = JsonDocument.Parse("""{"rateLimits":{"primary":null},"rateLimitsByLimitId":{}}""");
        Assert.Equal("UNSUPPORTED", Assert.Throws<CodexException>(() => CodexNormalizer.ParseRateLimits(doc.RootElement, Now)).Code);
    }

    [Fact]
    public void ProtocolMatchesRequestIdsAndClassifiesErrors()
    {
        Assert.False(CodexProtocol.TryMatchResponse("""{"method":"account/updated","params":{}}""", 3, out _));
        Assert.False(CodexProtocol.TryMatchResponse("""{"id":2,"result":{}}""", 3, out _));
        Assert.True(CodexProtocol.TryMatchResponse("""{"id":3,"result":{"rateLimits":{}}}""", 3, out var good));
        CodexProtocol.ThrowIfError(good);
        Assert.True(CodexProtocol.TryMatchResponse("""{"id":3,"error":{"code":-32600,"message":"codex account authentication required to read rate limits"}}""", 3, out var auth));
        Assert.Equal("AUTH_REQUIRED", Assert.Throws<CodexException>(() => CodexProtocol.ThrowIfError(auth)).Code);
        Assert.True(CodexProtocol.TryMatchResponse("""{"id":3,"error":{"code":429,"message":"rate limited"}}""", 3, out var limited));
        Assert.Equal("RATE_LIMITED", Assert.Throws<CodexException>(() => CodexProtocol.ThrowIfError(limited)).Code);
    }

    [Fact]
    public void FreshnessHonorsAgeFailureAndResetExpiry()
    {
        var c = new CodexContext { Snapshot = Snapshot(63, Now.AddMinutes(30), Now) };
        Assert.True(Freshness.IsFresh(c, Now.AddMinutes(2)));
        c.ErrorCode = "TIMEOUT";
        Assert.False(Freshness.IsFresh(c, Now.AddMinutes(2)));
        Assert.Contains("Stale", Freshness.DisplayState(c, Now.AddMinutes(2)));
        c.ErrorCode = null;
        Assert.False(Freshness.IsFresh(c, Now.AddMinutes(31)));
    }

    [Fact]
    public void SqliteRoundTripsNormalizedSnapshotAndSettings()
    {
        var folder = TempFolder();
        try
        {
            var store = new LocalStore(folder);
            var c = new CodexContext { Name = "Work", HomePath = Path.Combine(folder, "existing-home"), Snapshot = Snapshot(63, Now.AddDays(1), Now) };
            store.SaveContext(c);
            store.SaveSnapshot(c);
            var settings = new HubSettings { RefreshMinutes = 15, Theme = "Dark", Alert70 = false };
            store.SaveSettings(settings);
            var reopened = new LocalStore(folder);
            var loaded = Assert.Single(reopened.LoadContexts());
            Assert.Equal("Work", loaded.Name);
            Assert.Equal(63, Assert.Single(loaded.Snapshot!.Windows).UsedPercent);
            Assert.Equal("Dark", reopened.LoadSettings().Theme);
            Assert.False(reopened.LoadSettings().Alert70);
            Assert.False(File.Exists(Path.Combine(folder, "auth.json")));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ContextsAreIndependentAndRemovalKeepsCodexDirectory()
    {
        var folder = TempFolder();
        var first = Path.Combine(folder, "personal"); var second = Path.Combine(folder, "work");
        Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        try
        {
            using var service = new HubService(new LocalStore(Path.Combine(folder, "hub")));
            var a = service.AddContext(first, "Personal", refresh: false);
            var b = service.AddContext(second, "Work", refresh: false);
            Assert.Equal(2, service.Contexts.Count);
            Assert.Same(a, service.AddContext(first.ToUpperInvariant(), refresh: false));
            service.RenameContext(b, "Client");
            service.RemoveContext(a);
            Assert.True(Directory.Exists(first));
            Assert.Equal("Client", Assert.Single(service.Contexts).Name);
            Assert.True(new LocalStore(Path.Combine(folder, "hub")).IsIgnoredHome(first));
            service.AddContext(first, "Personal", refresh: false);
            Assert.False(new LocalStore(Path.Combine(folder, "hub")).IsIgnoredHome(first));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void CandidateScanStaysWithinConventionalProfileHomes()
    {
        var folder = TempFolder();
        var homeA = Path.Combine(folder, ".codex");
        var homeB = Path.Combine(folder, ".codex-client");
        var unrelated = Path.Combine(folder, "documents");
        Directory.CreateDirectory(homeA); Directory.CreateDirectory(homeB); Directory.CreateDirectory(unrelated);
        try
        {
            var result = CodexDiscovery.CandidateHomes(folder, homeB);
            Assert.Equal(2, result.Count);
            Assert.Contains(homeA, result);
            Assert.Contains(homeB, result);
            Assert.DoesNotContain(unrelated, result);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void RefreshPolicyRespectsResetAndBackoff()
    {
        var c = new CodexContext { Id = "steady" };
        var settings = new HubSettings { RefreshMinutes = 10 };
        var snapshot = Snapshot(63, Now.AddMinutes(3), Now);
        var next = RefreshPolicy.NextSuccess(c, snapshot, settings);
        Assert.InRange(next, Now.AddMinutes(3).AddSeconds(30), Now.AddMinutes(4));
        Assert.Equal(Now.AddMinutes(1), RefreshPolicy.NextFailure(Now, 1));
        Assert.Equal(Now.AddMinutes(8), RefreshPolicy.NextFailure(Now, 4));
        Assert.Equal(Now.AddMinutes(30), RefreshPolicy.NextFailure(Now, 6));
    }

    [Fact]
    public void AlertThresholdsDeduplicateAndResetRequiresObservedNewWindow()
    {
        var folder = TempFolder();
        try
        {
            var store = new LocalStore(folder);
            var c = new CodexContext { Name = "Personal" };
            var settings = new HubSettings();
            var old = Snapshot(68, Now.AddMinutes(2), Now.AddMinutes(-5));
            var high = Snapshot(86, Now.AddMinutes(2), Now);
            var first = AlertPolicy.Evaluate(c, old, high, settings, store, Now).ToArray();
            Assert.Single(first);
            Assert.Contains("85%", first[0].Body);
            Assert.Empty(AlertPolicy.Evaluate(c, old, high, settings, store, Now.AddSeconds(1)));
            var reset = Snapshot(2, Now.AddDays(7), Now.AddMinutes(3));
            var notices = AlertPolicy.Evaluate(c, high, reset, settings, store, Now.AddMinutes(3)).ToArray();
            Assert.Single(notices);
            Assert.Contains("reset", notices[0].Body);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void SameProviderIdentityAcrossContextsSharesAlertDedupe()
    {
        var folder = TempFolder();
        try
        {
            var store = new LocalStore(folder);
            var a = new CodexContext { Id = "a", Name = "One", IdentityFingerprint = "same-account" };
            var b = new CodexContext { Id = "b", Name = "Two", IdentityFingerprint = "same-account" };
            var high = Snapshot(86, Now.AddHours(1), Now);
            Assert.Single(AlertPolicy.Evaluate(a, null, high, new HubSettings(), store, Now));
            Assert.Empty(AlertPolicy.Evaluate(b, null, high, new HubSettings(), store, Now));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ThresholdWithoutResetRearmsAfterConfirmedDrop()
    {
        var folder = TempFolder();
        try
        {
            var store = new LocalStore(folder);
            var context = new CodexContext { Name = "Personal" };
            var settings = new HubSettings();
            var high = Snapshot(86, null, Now);
            Assert.Single(AlertPolicy.Evaluate(context, null, high, settings, store, Now));
            var low = Snapshot(60, null, Now.AddMinutes(1));
            Assert.Empty(AlertPolicy.Evaluate(context, high, low, settings, store, Now.AddMinutes(1)));
            Assert.Single(AlertPolicy.Evaluate(context, low, high, settings, store, Now.AddMinutes(2)));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task LiveCodexReadWhenExplicitlyEnabled()
    {
        if (Environment.GetEnvironmentVariable("AIUSAGE_LIVE_TEST") != "1") return;
        var reading = await new CodexConnector().ReadAsync(new CodexContext { HomePath = CodexDiscovery.DefaultHome }, CancellationToken.None);
        Assert.NotEmpty(reading.Snapshot.Windows);
        Assert.All(reading.Snapshot.Windows.Where(w => w.UsedPercent is not null), w => Assert.InRange(w.UsedPercent!.Value, 0, 100));
    }

    private static UsageSnapshot Snapshot(double percent, DateTimeOffset? reset, DateTimeOffset fetched)
        => new(fetched, "fixture", null, [new UsageWindow("codex:secondary", "WEEKLY", "Codex Weekly", percent, null, null, null, "%", reset, fetched, "fixture")]);
    private static string TempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AIUsageHub.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); return folder;
    }
}
