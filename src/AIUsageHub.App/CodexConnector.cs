using System.Diagnostics;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUsageHub;

public sealed class CodexException(string code, string? stage = null) : Exception(code)
{
    public string Code { get; } = code;
    public string? Stage { get; } = stage;
}

public sealed record CodexReading(string IdentityFingerprint, UsageSnapshot Snapshot);

public static class CodexDiscovery
{
    public static string DefaultHome => Path.GetFullPath(
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } value
            ? value : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));

    public static string? FindExecutable()
        => CandidateExecutables().FirstOrDefault();

    // Re-evaluate local installs on every check. A tray process can outlive changes to the
    // user PATH, and Codex may update the conventional hard-linked CLI entry point.
    public static IReadOnlyList<string> CandidateExecutables(string? userProfile = null, string? localAppData = null,
        IEnumerable<string?>? pathValues = null)
    {
        var profile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(Path.Combine(local, "Programs", "OpenAI", "Codex", "bin", "codex.exe"));
        AddVersioned(Path.Combine(local, "OpenAI", "Codex", "bin"));
        foreach (var home in CandidateHomes(profile, userProfile is null ? DefaultHome : Path.Combine(profile, ".codex")))
        {
            AddVersioned(Path.Combine(home, "packages", "standalone", "releases"), "bin");
            AddVersioned(Path.Combine(home, "packages", "app-server-daemon", "releases"), "bin");
        }
        pathValues ??= new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            ReadPath(EnvironmentVariableTarget.User),
            ReadPath(EnvironmentVariableTarget.Machine)
        };
        foreach (var pathValue in pathValues)
            foreach (var folder in (pathValue ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try { Add(Path.Combine(Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"')), "codex.exe")); }
                catch (ArgumentException) { }
            }
        return candidates;

        void AddVersioned(string root, string? bin = null)
        {
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(root).Take(20).OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase))
                    Add(Path.Combine(directory, bin ?? "", "codex.exe"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        void Add(string path)
        {
            try
            {
                if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) return;
                var full = Path.GetFullPath(path);
                if (seen.Add(full)) candidates.Add(full);
            }
            catch (ArgumentException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string? ReadPath(EnvironmentVariableTarget target)
    {
        try { return Environment.GetEnvironmentVariable("PATH", target); }
        catch { return null; }
    }

    // Metadata-only, bounded scan. Do not enumerate provider auth files or arbitrary drives.
    public static IReadOnlyList<string> CandidateHomes(string? userProfile = null, string? defaultHome = null)
    {
        var profile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string> { defaultHome ?? DefaultHome, Path.Combine(profile, ".codex") };
        try
        {
            candidates.AddRange(Directory.EnumerateDirectories(profile, ".codex*")
                .Take(20));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return candidates.Where(Directory.Exists).Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
    }

    public static async Task<string?> ReadVersionAsync(CancellationToken token)
    {
        var exe = FindExecutable();
        if (exe is null) return null;
        using var process = new Process { StartInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        } };
        process.StartInfo.ArgumentList.Add("--version");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        Task drain = Task.CompletedTask;
        try
        {
            process.Start();
            drain = BoundedText.DrainAsync(process.StandardError, timeout.Token);
            var line = await new BoundedLineReader(process.StandardOutput, 1024).ReadLineAsync(timeout.Token);
            return line is not null && Regex.IsMatch(line, @"^codex-cli\s+\d+\.\d+\.\d+([-.][a-zA-Z0-9.]+)?$") ? line : null;
        }
        catch { return null; }
        finally
        {
            await ChildProcess.StopAsync(process);
            timeout.Cancel();
            await drain;
        }
    }
}

public static class CodexNormalizer
{
    public static UsageSnapshot ParseRateLimits(JsonElement result, DateTimeOffset fetchedAt)
    {
        var windows = new List<UsageWindow>();
        string? plan = null;
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object)
        {
            foreach (var bucket in buckets.EnumerateObject())
                ParseBucket(bucket.Name, bucket.Value, fetchedAt, windows, ref plan);
        }
        if (windows.Count == 0 && result.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
            ParseBucket("codex", legacy, fetchedAt, windows, ref plan);
        if (windows.Count == 0) throw new CodexException("UNSUPPORTED");
        return new UsageSnapshot(fetchedAt, "Codex App Server · account/rateLimits/read", plan, windows);
    }

    private static void ParseBucket(string key, JsonElement bucket, DateTimeOffset fetchedAt, List<UsageWindow> windows, ref string? plan)
    {
        if (bucket.ValueKind != JsonValueKind.Object) return;
        var name = Text(bucket, "limitName") ?? (key.Equals("codex", StringComparison.OrdinalIgnoreCase) ? "Codex" : key.Replace('_', ' '));
        plan ??= Text(bucket, "planType");
        ParseWindow("primary");
        ParseWindow("secondary");
        if (bucket.TryGetProperty("credits", out var credits) && credits.ValueKind == JsonValueKind.Object)
        {
            var balance = Text(credits, "balance");
            var hasCredits = credits.TryGetProperty("hasCredits", out var available) && available.ValueKind == JsonValueKind.True;
            if (hasCredits && decimal.TryParse(balance, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount))
                windows.Add(new UsageWindow($"{key}:credits", "CREDITS", $"{name} credits", null, null, amount, null, "credits", null, fetchedAt, "Codex App Server"));
        }

        void ParseWindow(string field)
        {
            if (!bucket.TryGetProperty(field, out var raw) || raw.ValueKind != JsonValueKind.Object) return;
            if (!raw.TryGetProperty("usedPercent", out var percentage) || percentage.ValueKind != JsonValueKind.Number || !percentage.TryGetDouble(out var used) || used < 0 || used > 100)
                return;
            int? duration = raw.TryGetProperty("windowDurationMins", out var durationValue) && durationValue.ValueKind == JsonValueKind.Number && durationValue.TryGetInt32(out var mins) && mins > 0 ? mins : null;
            var kind = duration switch { 300 => "FIVE_HOUR", 1440 => "DAILY", 10080 => "WEEKLY", _ => "UNKNOWN" };
            var period = duration switch { 300 => "5-hour", 1440 => "Daily", 10080 => "Weekly", > 0 => $"{duration} min", _ => field == "primary" ? "Primary" : "Secondary" };
            DateTimeOffset? reset = null;
            if (raw.TryGetProperty("resetsAt", out var resetValue) && resetValue.ValueKind == JsonValueKind.Number && resetValue.TryGetInt64(out var seconds))
            {
                try { reset = DateTimeOffset.FromUnixTimeSeconds(seconds); } catch (ArgumentOutOfRangeException) { }
            }
            windows.Add(new UsageWindow($"{key}:{field}", kind, $"{name} {period}", used, null, null, null, "%", reset, fetchedAt, "Codex App Server"));
        }
    }

    private static string? Text(JsonElement obj, string name) => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public static class CodexProtocol
{
    public static bool TryMatchResponse(string line, int id, out JsonElement response)
    {
        if (line.Length > 2_000_000) throw new CodexException("UNSUPPORTED");
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var rawId) &&
            rawId.ValueKind == JsonValueKind.Number && rawId.TryGetInt32(out var responseId) && responseId == id)
        {
            response = root.Clone();
            return true;
        }
        response = default;
        return false;
    }

    public static void ThrowIfError(JsonElement response)
    {
        if (!response.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return;
        var message = error.TryGetProperty("message", out var raw) && raw.ValueKind == JsonValueKind.String ? raw.GetString() ?? "" : "";
        if (message.Contains("authentication", StringComparison.OrdinalIgnoreCase) || message.Contains("login", StringComparison.OrdinalIgnoreCase))
            throw new CodexException("AUTH_REQUIRED");
        if (message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) || message.Contains("too many", StringComparison.OrdinalIgnoreCase))
            throw new CodexException("RATE_LIMITED");
        throw new CodexException("PROVIDER_ERROR");
    }
}

public sealed class CodexConnector
{
    private readonly Func<IReadOnlyList<string>> _executableCandidates;
    public CodexConnector(Func<IReadOnlyList<string>>? executableCandidates = null)
        => _executableCandidates = executableCandidates ?? (() => CodexDiscovery.CandidateExecutables());

    public string? ExecutablePath => _executableCandidates().FirstOrDefault();

    public async Task<CodexReading> ReadAsync(CodexContext context, CancellationToken cancellationToken)
    {
        var executables = _executableCandidates();
        if (executables.Count == 0) throw new CodexException("CODEX_NOT_FOUND");
        if (!Directory.Exists(context.HomePath)) throw new CodexException("CONTEXT_MISSING");
        using var process = StartAppServer(executables, context.HomePath);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var stderr = BoundedText.DrainAsync(process.StandardError, timeout.Token);
        var reader = new BoundedLineReader(process.StandardOutput, 2_000_000);
        var stage = "initialize";
        try
        {
            await SendAsync(process, new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "ai-usage-hub", version = "1.0.0" } } }, timeout.Token);
            var init = await ReceiveAsync(reader, 1, timeout.Token);
            CodexProtocol.ThrowIfError(init);
            stage = "account";
            await SendAsync(process, new { method = "initialized" }, timeout.Token);
            await SendAsync(process, new { id = 2, method = "account/read", @params = new { refreshToken = false } }, timeout.Token);
            var accountResponse = await ReceiveAsync(reader, 2, timeout.Token);
            CodexProtocol.ThrowIfError(accountResponse);
            if (!accountResponse.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
                throw new CodexException("UNSUPPORTED");
            if (!result.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object)
                throw new CodexException("AUTH_REQUIRED");
            if (!account.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "chatgpt")
                throw new CodexException("UNSUPPORTED");
            var identity = account.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String
                ? email.GetString()?.Trim().ToLowerInvariant() : null;
            // Some Codex accounts omit email. Keep those contexts distinct and provisional.
            var fingerprint = string.IsNullOrWhiteSpace(identity) ? "context:" + context.Id
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("chatgpt:" + identity)));
            stage = "rate-limits";
            await SendAsync(process, new { id = 3, method = "account/rateLimits/read", @params = new { } }, timeout.Token);
            var limits = await ReceiveAsync(reader, 3, timeout.Token);
            CodexProtocol.ThrowIfError(limits);
            if (!limits.TryGetProperty("result", out var usage) || usage.ValueKind != JsonValueKind.Object)
                throw new CodexException("UNSUPPORTED");
            return new CodexReading(fingerprint, CodexNormalizer.ParseRateLimits(usage, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new CodexException("TIMEOUT", stage); }
        catch (EndOfStreamException) { throw new CodexException("PROVIDER_ERROR"); }
        catch (JsonException) { throw new CodexException("UNSUPPORTED"); }
        catch (IOException) { throw new CodexException("PROVIDER_ERROR"); }
        finally
        {
            await ChildProcess.StopAsync(process);
            timeout.Cancel();
            await stderr;
        }
    }

    private static Process StartAppServer(IReadOnlyList<string> executables, string homePath)
    {
        Exception? lastError = null;
        foreach (var exe in executables)
        {
            Process? process = null;
            try
            {
                var start = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false), StandardInputEncoding = new UTF8Encoding(false)
                };
                start.ArgumentList.Add("app-server");
                start.ArgumentList.Add("--stdio");
                start.Environment["CODEX_HOME"] = homePath;
                foreach (var key in new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN" }) start.Environment.Remove(key);
                process = new Process { StartInfo = start };
                if (process.Start()) return process;
                lastError = new InvalidOperationException("Process.Start returned false");
            }
            catch (Exception error) { lastError = error; }
            process?.Dispose();
        }
        var detail = lastError is Win32Exception win32 ? $"win32:{win32.NativeErrorCode:X8}"
            : lastError is null ? "unknown" : $"{lastError.GetType().Name}:{lastError.HResult:X8}";
        throw new CodexException("CODEX_LAUNCH_FAILED", detail);
    }

    private static async Task SendAsync(Process process, object message, CancellationToken token)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), token);
        await process.StandardInput.FlushAsync(token);
    }

    private static async Task<JsonElement> ReceiveAsync(BoundedLineReader reader, int id, CancellationToken token)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(token) ?? throw new EndOfStreamException();
            if (CodexProtocol.TryMatchResponse(line, id, out var response)) return response;
        }
    }
}
