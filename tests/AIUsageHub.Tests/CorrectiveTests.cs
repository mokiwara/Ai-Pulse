using AIUsageHub;
using System.Text.Json;
using System.IO;
namespace AIUsageHub.Tests;

public sealed class CorrectiveTests
{
    [Fact]
    public void CodexDiscoveryFindsCurrentInstallVersionedInstallAndUpdatedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "AIPulseDiscovery", Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(root, "profile");
        var local = Path.Combine(root, "local");
        var pathBin = Path.Combine(root, "new-path-bin");
        var installed = Path.Combine(local, "Programs", "OpenAI", "Codex", "bin", "codex.exe");
        var versioned = Path.Combine(profile, ".codex", "packages", "standalone", "releases", "0.157.0", "bin", "codex.exe");
        var onPath = Path.Combine(pathBin, "codex.exe");
        try
        {
            foreach (var file in new[] { installed, versioned, onPath })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, "test");
            }
            var candidates = CodexDiscovery.CandidateExecutables(profile, local, [pathBin + Path.PathSeparator + pathBin]);
            Assert.Equal(new[] { installed, versioned, onPath }, candidates);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CodexLaunchTriesNextCandidateAndDistinguishesLaunchFailures()
    {
        var root = Path.Combine(Path.GetTempPath(), "AIPulseLaunch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var invalid = Path.Combine(root, "invalid.exe");
        File.WriteAllText(invalid, "not a Windows executable");
        try
        {
            var context = new CodexContext { HomePath = root };
            var launchError = await Assert.ThrowsAsync<CodexException>(() =>
                new CodexConnector(() => [invalid]).ReadAsync(context, CancellationToken.None));
            Assert.Equal("CODEX_LAUNCH_FAILED", launchError.Code);
            Assert.Matches(@"^(?:win32|[A-Za-z]+):[0-9A-F]{8}$", launchError.Stage!);

            var where = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe");
            Assert.True(File.Exists(where));
            var protocolError = await Assert.ThrowsAsync<CodexException>(() =>
                new CodexConnector(() => [invalid, where]).ReadAsync(context, CancellationToken.None));
            Assert.NotEqual("CODEX_LAUNCH_FAILED", protocolError.Code);
            Assert.NotEqual("CODEX_NOT_FOUND", protocolError.Code);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CodexAvailabilityFailuresRetrySoonAndLogOnlySafeLaunchMetadata()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1790200000);
        Assert.Equal(now.AddMinutes(1), RefreshPolicy.NextFailure(now, 1, "CODEX_NOT_FOUND"));
        Assert.Equal(now.AddMinutes(4), RefreshPolicy.NextFailure(now, 3, "CODEX_LAUNCH_FAILED"));
        Assert.Equal(now.AddMinutes(5), RefreshPolicy.NextFailure(now, 6, "CODEX_LAUNCH_FAILED"));
        Assert.Equal(now.AddMinutes(30), RefreshPolicy.NextFailure(now, 6, "PROVIDER_ERROR"));

        var folder = Path.Combine(Path.GetTempPath(), "AIPulseLog", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            SafeLog.Write(folder, "CODEX_LAUNCH_FAILED", "account", "win32:00000005");
            SafeLog.Write(folder, "CODEX_LAUNCH_FAILED", "account", "secret /private/path");
            var lines = File.ReadAllLines(Path.Combine(folder, "hub.log"));
            Assert.Contains("detail=win32:00000005", lines[0]);
            Assert.DoesNotContain("detail=", lines[1]);
            Assert.DoesNotContain("secret", string.Join('\n', lines));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void CollapsedNotchUsesTheChosenAccountAndMainUsageWindows()
    {
        var now = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        var other = new CodexContext { Provider = "Codex", Name = "Other", IdentityFingerprint = "other",
            Snapshot = new UsageSnapshot(now, "test", null,
                [UsageWindow.FromRemaining("codex:primary", "FIVE_HOUR", "5-hour", 1, now, "test", now.AddHours(1))]) };
        var chosen = new CodexContext { Provider = "Codex", Name = "Work", IdentityFingerprint = "work",
            Snapshot = new UsageSnapshot(now, "test", null,
                [UsageWindow.FromRemaining("gpt-reserve:secondary", "WEEKLY", "Reserve weekly", 5, now, "test", now.AddDays(1)),
                 UsageWindow.FromRemaining("codex:primary", "FIVE_HOUR", "5-hour", 80, now, "test", now.AddHours(1)),
                 UsageWindow.FromRemaining("codex:secondary", "WEEKLY", "Weekly", 32, now, "test", now.AddDays(1))]) };

        var reading = NotchPolicy.CapsuleReading([other, chosen], "Codex", ContextIdentity.AccountKey(chosen), now)!;
        Assert.Same(chosen, reading.Account);
        Assert.Equal("80/32%", reading.Text);
        Assert.Equal(32, reading.LowestRemaining);
        Assert.Contains("5-hour 80% left", reading.ToolTip);
        Assert.Contains("Weekly 32% left", reading.ToolTip);
        Assert.Null(NotchPolicy.CapsuleReading([other, chosen], "Codex", HubSettings.HiddenNotchAccount, now));
    }

    [Fact]
    public void CollapsedNotchShowsOnlyCurrentWeeklyLimitWhenFiveHourIsAbsentOrExpired()
    {
        var now = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        var account = new CodexContext { Provider = "Claude", Snapshot = new UsageSnapshot(now, "test", null,
            [UsageWindow.FromRemaining("five", "FIVE_HOUR", "5-hour", 70, now, "test", now.AddMinutes(-1)),
             UsageWindow.FromRemaining("week", "WEEKLY", "Weekly", 42, now, "test", now.AddDays(1))]) };
        Assert.Equal("42%", NotchPolicy.CapsuleReading([account], "Claude", null, now)!.Text);
        account.ErrorCode = "TIMEOUT";
        Assert.Equal("—", NotchPolicy.CapsuleReading([account], "Claude", null, now)!.Text);
    }

    [Fact]
    public void NotchAccountChoicesAndOneMinuteRefreshPersist()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AIPulseTest", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(folder);
            var settings = new HubSettings { CodexNotchAccountKey = "work", ClaudeNotchAccountKey = HubSettings.HiddenNotchAccount, RefreshMinutes = 1 };
            store.SaveSettings(settings);
            var loaded = new LocalStore(folder).LoadSettings();
            Assert.Equal("work", loaded.CodexNotchAccountKey);
            Assert.Equal(HubSettings.HiddenNotchAccount, loaded.ClaudeNotchAccountKey);
            Assert.Equal(1, loaded.RefreshMinutes);
            var context = new CodexContext { Id = "work" };
            var snapshot = new UsageSnapshot(DateTimeOffset.UtcNow, "test", null, []);
            Assert.InRange(RefreshPolicy.NextSuccess(context, snapshot, loaded) - snapshot.FetchedAt,
                TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(70));
            loaded.RefreshMinutes = 30;
            loaded.Normalize();
            Assert.Equal(15, loaded.RefreshMinutes);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void CodexLaunchFailureDoesNotClaimTheCliIsMissing()
    {
        var context = new CodexContext { ErrorCode = "CODEX_LAUNCH_FAILED" };
        Assert.Equal("Could not start Codex CLI", Freshness.DisplayState(context, DateTimeOffset.UtcNow));
        Assert.Contains("retry automatically", Freshness.ErrorText(context.ErrorCode));
        context.Snapshot = new UsageSnapshot(DateTimeOffset.UtcNow, "test", null,
            [UsageWindow.FromRemaining("weekly", "WEEKLY", "Weekly", 50, DateTimeOffset.UtcNow, "test")]);
        Assert.StartsWith("Stale", Freshness.DisplayState(context, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void LocalClaudeUsageProvidesSharedPlanWindowsWithoutModelTurns()
    {
        var now = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
        var output = JsonSerializer.Serialize(new
        {
            local_command = "usage", num_turns = 0, total_cost_usd = 0, is_error = false,
            result = "Current session: 28% used · resets Sep 25, 4pm (Africa/Casablanca)\n" +
                     "Current week (all models): 41% used · resets Oct 2, 4am (Africa/Casablanca)\n" +
                     "Last 24h · 632 requests · 7 sessions"
        });
        var snapshot = ClaudeConnector.ParseUsageCommand(output, now);
        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot.Windows.Count);
        Assert.Equal(72, snapshot.Windows[0].RemainingPercent);
        Assert.Equal(59, snapshot.Windows[1].RemainingPercent);
        Assert.All(snapshot.Windows, window => Assert.True(window.ResetsAt > now));
        Assert.Equal("Claude Code /usage", snapshot.Source);
    }

    [Theory]
    [InlineData("chat", 0, 0)]
    [InlineData("usage", 1, 0)]
    [InlineData("usage", 0, 0.01)]
    public void ClaudeUsageRejectsNonLocalOrBillableResults(string command, int turns, double cost)
    {
        var output = JsonSerializer.Serialize(new
        {
            local_command = command, num_turns = turns, total_cost_usd = cost, is_error = false,
            result = "Current session: 25% used · resets Sep 25, 4pm (Africa/Casablanca)"
        });
        Assert.Null(ClaudeConnector.ParseUsageCommand(output, new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void UserComposedStatusLineIsNeverOverwrittenOrRemoved()
    {
        var root = Path.Combine(Path.GetTempPath(), "HubStatusLine", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var context = new CodexContext { Provider = "Claude", HomePath = root };
            var path = Path.Combine(root, "settings.json");
            var json = JsonSerializer.Serialize(new { statusLine = new { type = "command", command = ClaudeConnector.StatusLineCommand(context.Id) + " | my-formatter" } });
            File.WriteAllText(path, json);
            Assert.False(ClaudeConnector.EnsureStatusLine(context));
            ClaudeConnector.RemoveStatusLine(context);
            Assert.Equal(json, File.ReadAllText(path));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void SmallPositiveCapacityIsNotRoundedToZero()
    {
        Assert.Equal("<1%", UsageWindow.FormatPercent(.2));
        Assert.Equal("0%", UsageWindow.FormatPercent(0));
        Assert.Equal("—", UsageWindow.FormatPercent(null));
    }
    [Fact]
    public void LoginBelongsToOfficialCliAndScopesOnlyChildEnvironment()
    {
        var parent = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var start = ClaudeConnector.LoginStartInfo(@"C:\Program Files\Claude\claude.exe", @"C:\Contexts\Work ' & space");
        Assert.Equal(new[] { "auth", "login", "--claudeai" }, start.ArgumentList);
        Assert.Equal(@"C:\Contexts\Work ' & space", start.Environment["CLAUDE_CONFIG_DIR"]);
        Assert.False(start.UseShellExecute);
        Assert.False(start.CreateNoWindow);
        Assert.False(start.RedirectStandardInput);
        Assert.False(start.RedirectStandardOutput);
        Assert.False(start.RedirectStandardError);
        Assert.Equal(parent, Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"));
    }

    [Theory]
    [InlineData(null)] [InlineData(-1d)] [InlineData(101d)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidCapacityNeverBecomesZero(double? used)
    {
        Assert.Null(new UsageWindow("x", "WEEKLY", "Weekly", used, null, null, null, "%", null, DateTimeOffset.UtcNow, "test").RemainingPercent);
    }

    [Theory]
    [InlineData("{}")] [InlineData("{\"usedPercent\":null}")] [InlineData("{\"usedPercent\":-1}")] [InlineData("{\"usedPercent\":101}")]
    public void CodexMissingOrInvalidIsUnavailable(string window)
    {
        using var json = JsonDocument.Parse("{\"rateLimits\":{\"primary\":" + window + "}}");
        Assert.Throws<CodexException>(() => CodexNormalizer.ParseRateLimits(json.RootElement, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SummaryOmitsStaleFailuresAndExpiredCapacity()
    {
        var now = DateTimeOffset.UtcNow;
        var c = new CodexContext { Name = "Work", Snapshot = new(now, "test", null, [UsageWindow.FromRemaining("x", "WEEKLY", "Weekly", 0, now, "test", now.AddDays(1))]) };
        Assert.Contains("0% left", NotchPolicy.Summary([c], now));
        Assert.DoesNotContain("0%", NotchPolicy.Summary([c], now.AddMinutes(16)));
        c.ErrorCode = "TIMEOUT";
        Assert.DoesNotContain("0%", NotchPolicy.Summary([c], now));
        c.ErrorCode = null;
        Assert.DoesNotContain("0%", NotchPolicy.Summary([c], now.AddDays(2)));
    }

    [Fact]
    public async Task LoggedOutFeedCannotMasqueradeAsAuthenticated()
    {
        var root = Path.Combine(Path.GetTempPath(), "HubCorrection", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var context = new CodexContext { Provider = "Claude", HomePath = root, ErrorCode = "CLAUDE_AUTH_REQUIRED", LastAttemptAt = DateTimeOffset.UtcNow };
        try
        {
            using var service = new HubService(new LocalStore(Path.Combine(root, "hub")));
            service.Contexts.Add(context);
            ClaudeConnector.CaptureStatusLine(context.Id, """{"rate_limits":{"five_hour":{"used_percentage":100}}}""");
            await service.RefreshClaudeContextAsync(context, false);
            Assert.Null(context.Snapshot);
            Assert.Equal("CLAUDE_AUTH_REQUIRED", context.ErrorCode);
        }
        finally { File.Delete(ClaudeConnector.FeedPath(context.Id)); Directory.Delete(root, true); }
    }
}
