using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AIUsageHub;

// Claude Code's documented status-line JSON is an event-driven subscription-usage feed.
// The auth probe never reads credential files and never sends a model request.
public sealed class ClaudeConnector
{
    // The visible CLI owns stdin, stdout, browser callbacks and credential persistence.
    // Hub only selects the requested context and observes auth status afterward.
    public static ProcessStartInfo LoginStartInfo(string executable, string home)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = false,
            WorkingDirectory = home
        };
        start.ArgumentList.Add("auth");
        start.ArgumentList.Add("login");
        start.ArgumentList.Add("--claudeai");
        start.Environment["CLAUDE_CONFIG_DIR"] = home;
        return start;
    }

    public string? ExecutablePath => FindExecutable();
    public static string DefaultHome => Path.GetFullPath(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } custom
        ? custom : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"));

    public static string? FindExecutable()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        if (File.Exists(local)) return local;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Path.Combine(p.Trim('"'), "claude.exe")).FirstOrDefault(p => Path.IsPathFullyQualified(p) && File.Exists(p));
    }

    public static IReadOnlyList<string> CandidateHomes(string? profile = null, string? defaultHome = null)
    {
        profile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var paths = new List<string> { defaultHome ?? DefaultHome, Path.Combine(profile, ".claude") };
        try { paths.AddRange(Directory.EnumerateDirectories(profile, ".claude*").Take(20)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return paths.Where(Directory.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
    }

    public async Task<(string? Error, string? Identity, string? Plan)> ProbeAsync(CodexContext context, CancellationToken token)
    {
        var exe = ExecutablePath;
        if (exe is null) return ("CLAUDE_NOT_FOUND", null, null);
        if (!Directory.Exists(context.HomePath)) return ("CONTEXT_MISSING", null, null);
        var fileVersion = FileVersionInfo.GetVersionInfo(exe).FileVersion;
        if (Version.TryParse(fileVersion, out var version) && version < new Version(2, 1, 251))
            return ("CLAUDE_UNSUPPORTED", null, null);
        using var process = new Process { StartInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        } };
        process.StartInfo.ArgumentList.Add("auth");
        process.StartInfo.ArgumentList.Add("status");
        process.StartInfo.Environment["CLAUDE_CONFIG_DIR"] = context.HomePath;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        Task stderr = Task.CompletedTask;
        try
        {
            process.Start();
            stderr = BoundedText.DrainAsync(process.StandardError, timeout.Token);
            var output = await BoundedText.ReadAsync(process.StandardOutput, 100_000, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await stderr; // drain without retaining potentially sensitive diagnostics
            if (output.Length > 100_000) return ("CLAUDE_UNSUPPORTED", null, null);
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            if (!root.TryGetProperty("loggedIn", out var logged) || logged.ValueKind != JsonValueKind.True)
                return ("CLAUDE_AUTH_REQUIRED", null, null);
            var method = GetString(root, "authMethod");
            if (string.Equals(method, "apiKey", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(method, "api_key", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
                return ("CLAUDE_API_AUTH", null, null);
            // Only provider supplied stable IDs establish duplicate identity. Email alone can
            // represent different organization seats and is deliberately not used to merge.
            var accountId = GetString(root, "accountId") ?? GetString(root, "userId");
            var orgId = GetString(root, "organizationId") ?? GetString(root, "orgId") ?? "personal";
            var identity = accountId is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"claude:{accountId}:{orgId}")));
            return (null, identity, GetString(root, "subscriptionType"));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return ("TIMEOUT", null, null); }
        catch { return ("CLAUDE_UNSUPPORTED", null, null); }
        finally { await ChildProcess.StopAsync(process); timeout.Cancel(); await stderr; }
    }

    private static string? GetString(JsonElement obj, string key) => obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // /usage is a local Claude Code command. Its print-mode result is text, so only
    // accept the two subscription rows and require the CLI to confirm zero model turns.
    public async Task<UsageSnapshot?> ReadUsageAsync(CodexContext context, CancellationToken token)
    {
        var exe = ExecutablePath;
        if (exe is null || !Directory.Exists(context.HomePath)) return null;
        using var process = new Process { StartInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        } };
        process.StartInfo.ArgumentList.Add("-p");
        process.StartInfo.ArgumentList.Add("/usage");
        process.StartInfo.ArgumentList.Add("--output-format");
        process.StartInfo.ArgumentList.Add("json");
        // Disable extensions and model turns before starting a local usage-only command.
        foreach (var arg in new[] { "--max-turns", "0", "--safe-mode", "--tools", "",
            "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}", "--settings",
            "{\"disableAllHooks\":true}", "--setting-sources", "", "--no-session-persistence", "--no-chrome" })
            process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.WorkingDirectory = AppContext.BaseDirectory;
        process.StartInfo.Environment["CLAUDE_CONFIG_DIR"] = context.HomePath;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        Task stderr = Task.CompletedTask;
        try
        {
            process.Start();
            stderr = BoundedText.DrainAsync(process.StandardError, timeout.Token);
            var output = await BoundedText.ReadAsync(process.StandardOutput, 100_000, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await stderr;
            return process.ExitCode == 0 && output.Length <= 100_000
                ? ParseUsageCommand(output, DateTimeOffset.UtcNow) : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return null; }
        finally { await ChildProcess.StopAsync(process); timeout.Cancel(); await stderr; }
    }

    public static UsageSnapshot? ParseUsageCommand(string json, DateTimeOffset observedAt)
    {
        if (json.Length > 100_000) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                GetString(root, "local_command") != "usage" ||
                !root.TryGetProperty("num_turns", out var turns) || turns.ValueKind != JsonValueKind.Number || !turns.TryGetInt32(out var turnCount) || turnCount != 0 ||
                !root.TryGetProperty("total_cost_usd", out var cost) || cost.ValueKind != JsonValueKind.Number || cost.GetDouble() != 0 ||
                !root.TryGetProperty("is_error", out var error) || error.ValueKind != JsonValueKind.False ||
                GetString(root, "result") is not { } result) return null;
            var windows = new List<UsageWindow>();
            foreach (var line in result.Split('\n'))
            {
                var match = Regex.Match(line.Trim(),
                    @"^(?<name>Current session|Current week \(all models\)):\s*(?<used>\d+(?:\.\d+)?)% used(?:\s*[·\-]\s*resets\s+(?<reset>.+))?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success || !double.TryParse(match.Groups["used"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var used) ||
                    !double.IsFinite(used) || used is < 0 or > 100) continue;
                var weekly = match.Groups["name"].Value.StartsWith("Current week", StringComparison.OrdinalIgnoreCase);
                var kind = weekly ? "WEEKLY" : "FIVE_HOUR";
                var reset = ParseUsageReset(match.Groups["reset"].Value, observedAt);
                if (reset <= observedAt) continue;
                windows.Add(new UsageWindow(weekly ? "claude:seven_day" : "claude:five_hour", kind,
                    weekly ? "Weekly" : "5-hour", used, null, null, null, "%", reset, observedAt, "Claude Code /usage"));
            }
            return windows.Count == 0 ? null : new UsageSnapshot(observedAt, "Claude Code /usage", null, windows);
        }
        catch (JsonException) { return null; }
        catch (FormatException) { return null; }
    }

    private static DateTimeOffset? ParseUsageReset(string input, DateTimeOffset observedAt)
    {
        var match = Regex.Match(input.Trim(),
            @"^(?<month>[A-Za-z]+)\s+(?<day>\d{1,2}),\s*(?<hour>\d{1,2})(?::(?<minute>\d{2}))?(?<period>am|pm)(?:\s+\((?<zone>[^)]+)\))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        var names = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames;
        var month = Array.FindIndex(names, name => name.Equals(match.Groups["month"].Value, StringComparison.OrdinalIgnoreCase)) + 1;
        if (month == 0 || !int.TryParse(match.Groups["day"].Value, out var day) ||
            !int.TryParse(match.Groups["hour"].Value, out var hour) || hour is < 1 or > 12 ||
            !int.TryParse(match.Groups["minute"].Success ? match.Groups["minute"].Value : "0", out var minute) || minute > 59)
            return null;
        hour = hour % 12 + (match.Groups["period"].Value.Equals("pm", StringComparison.OrdinalIgnoreCase) ? 12 : 0);
        TimeZoneInfo zone;
        try { zone = match.Groups["zone"].Success ? TimeZoneInfo.FindSystemTimeZoneById(match.Groups["zone"].Value) : TimeZoneInfo.Local; }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
        var localNow = TimeZoneInfo.ConvertTime(observedAt, zone);
        for (var year = localNow.Year; year <= localNow.Year + 1; year++)
        {
            DateTime local;
            try { local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified); }
            catch (ArgumentOutOfRangeException) { continue; }
            if (zone.IsInvalidTime(local)) continue;
            var reset = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
            if (reset > observedAt && reset <= observedAt.AddDays(8)) return reset;
        }
        return null;
    }

    public static UsageSnapshot? ParseStatusLine(string json, DateTimeOffset observedAt)
    {
        if (json.Length > 2_000_000) return null;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object) return null;
        var windows = new List<UsageWindow>();
        Add("five_hour", "FIVE_HOUR", "5-hour");
        Add("seven_day", "WEEKLY", "Weekly");
        return windows.Count == 0 ? null : new UsageSnapshot(observedAt, "Claude Code status line", null, windows);

        void Add(string field, string kind, string title)
        {
            if (!limits.TryGetProperty(field, out var raw) || raw.ValueKind != JsonValueKind.Object) return;
            if (!raw.TryGetProperty("used_percentage", out var percentage) || percentage.ValueKind != JsonValueKind.Number ||
                !percentage.TryGetDouble(out var used) || !double.IsFinite(used) || used < 0 || used > 100) return;
            DateTimeOffset? reset = null;
            if (raw.TryGetProperty("resets_at", out var rawReset) && rawReset.ValueKind == JsonValueKind.Number && rawReset.TryGetInt64(out var seconds))
            { try { reset = DateTimeOffset.FromUnixTimeSeconds(seconds); } catch (ArgumentOutOfRangeException) { } }
            if (reset <= observedAt) return; // Claude drops expired windows; never replay one.
            windows.Add(new UsageWindow($"claude:{field}", kind, title, used, null, null, null, "%", reset,
                observedAt, "Claude Code status line"));
        }
    }

    public static string FeedPath(string contextId)
    {
        if (!Guid.TryParseExact(contextId, "N", out _)) throw new ArgumentException("Invalid context identifier.", nameof(contextId));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsageHub", "claude-feed", contextId + ".json");
    }

    public static string? CaptureStatusLine(string contextId, string input)
    {
        if (!Guid.TryParseExact(contextId, "N", out _)) return null;
        UsageSnapshot? snapshot;
        try { snapshot = ParseStatusLine(input, DateTimeOffset.UtcNow); }
        catch (JsonException) { return null; }
        if (snapshot is null) return null;
        var path = FeedPath(contextId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
            File.Move(temp, path, true);
        }
        finally { try { File.Delete(temp); } catch { } }
        return string.Join("  ", snapshot.Windows.Select(w => $"{w.Title} {UsageWindow.FormatPercent(w.RemainingPercent)} left"));
    }

    public static string StatusLineCommand(string contextId)
    {
        if (!Guid.TryParseExact(contextId, "N", out _)) throw new ArgumentException("Invalid context identifier.", nameof(contextId));
        var exe = Environment.ProcessPath ?? "";
        if (Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            return $"\"{exe}\" \"{Path.Combine(AppContext.BaseDirectory, "AIUsageHub.App.dll")}\" --claude-statusline {contextId}";
        return $"\"{exe}\" --claude-statusline {contextId}";
    }

    public static bool EnsureStatusLine(CodexContext context)
    {
        var path = Path.Combine(context.HomePath, "settings.json");
        JsonObject settings;
        string? original;
        try { original = File.Exists(path) ? BoundedText.ReadFileAsync(path, 2_000_000, default).GetAwaiter().GetResult() : null; settings = original is null ? new JsonObject() : JsonNode.Parse(original)?.AsObject() ?? new JsonObject(); }
        catch { return false; }
        if (settings["statusLine"] is JsonObject existing)
        {
            if (existing["command"] is not JsonValue commandValue || !commandValue.TryGetValue<string>(out var command)) return false;
            if (command == StatusLineCommand(context.Id)) return true;
            // Only relocate a direct Hub-owned command. Never overwrite a user's pipeline.
            if (!OwnsStatusLine(command, context.Id)) return false;
        }
        else if (settings["statusLine"] is not null) return false;
        settings["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = StatusLineCommand(context.Id) };
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".aiusagehub.tmp";
        try
        {
            File.WriteAllText(temp, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            // Refuse to replace a configuration changed while it was being prepared.
            var current = File.Exists(path) ? BoundedText.ReadFileAsync(path, 2_000_000, default).GetAwaiter().GetResult() : null;
            if (current != original) { File.Delete(temp); return false; }
            if (original is not null && !File.Exists(path + ".aipulse-backup")) File.WriteAllText(path + ".aipulse-backup", original);
            File.Move(temp, path, true);
            return true;
        }
        catch { try { File.Delete(temp); } catch { } return false; }
    }

    private static bool OwnsStatusLine(string command, string contextId) => System.Text.RegularExpressions.Regex.IsMatch(command,
        "^\"[^\"]+\"(?: \"[^\"]+\\.dll\")? --claude-statusline " + System.Text.RegularExpressions.Regex.Escape(contextId) + "$");

    public static void RemoveStatusLine(CodexContext context, bool currentExecutableOnly = false)
    {
        var path = Path.Combine(context.HomePath, "settings.json");
        string? temp = null;
        try
        {
            if (!File.Exists(path)) return;
            var original = BoundedText.ReadFileAsync(path, 2_000_000, default).GetAwaiter().GetResult();
            var settings = JsonNode.Parse(original)?.AsObject();
            if (settings?["statusLine"] is not JsonObject line ||
                !OwnsStatusLine(line["command"]?.GetValue<string>() ?? "", context.Id)) return;
            if (currentExecutableOnly && line["command"]?.GetValue<string>() != StatusLineCommand(context.Id)) return;
            settings.Remove("statusLine");
            temp = path + "." + Guid.NewGuid().ToString("N") + ".aiusagehub.tmp";
            File.WriteAllText(temp, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            if (!File.Exists(path) || BoundedText.ReadFileAsync(path, 2_000_000, default).GetAwaiter().GetResult() != original) return;
            File.Move(temp, path, true);
        }
        catch { /* Never damage Claude settings to complete removal. */ }
        finally { if (temp is not null) { try { File.Delete(temp); } catch { } } }
    }
}
