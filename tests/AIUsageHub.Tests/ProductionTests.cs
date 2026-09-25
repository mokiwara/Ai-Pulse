using System.IO;
using AIUsageHub;

namespace AIUsageHub.Tests;

public sealed class ProductionTests
{
    [Fact] public void CaptureRemovalPreservesOversizedSettingsAndLeavesNoTemporaryFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "AIPulseTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var context = new CodexContext { Provider = "Claude", HomePath = root };
            var path = Path.Combine(root, "settings.json");
            var original = System.Text.Json.JsonSerializer.Serialize(new
            {
                statusLine = new { type = "command", command = ClaudeConnector.StatusLineCommand(context.Id) },
                largeSetting = new string('x', 2_000_001)
            });
            File.WriteAllText(path, original);
            ClaudeConnector.RemoveStatusLine(context);
            Assert.Equal(original, File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact] public async Task OversizedOutputStopsBeforeBufferingTheEntireInput()
    {
        using var reader = new StringReader(new string('x', 100_000));
        await Assert.ThrowsAsync<InvalidDataException>(() => BoundedText.ReadAsync(reader, 1024, CancellationToken.None));
        Assert.Equal('x', (char)reader.Read()); // The rest was never retained.
    }

    [Fact] public async Task BoundedLinesPreserveReadAheadAcrossResponses()
    {
        using var source = new StringReader("one\r\ntwo\nlast");
        var reader = new BoundedLineReader(source, 4);
        Assert.Equal("one", await reader.ReadLineAsync(default));
        Assert.Equal("two", await reader.ReadLineAsync(default));
        Assert.Equal("last", await reader.ReadLineAsync(default));
        Assert.Null(await reader.ReadLineAsync(default));
        await Assert.ThrowsAsync<InvalidDataException>(() => new BoundedLineReader(new StringReader("12345"), 4).ReadLineAsync(default));
    }

    [Fact] public async Task CancellationWaitsForOperationsBeforeDisposingResources()
    {
        var lifetime = new AsyncLifetime();
        var lease = lifetime.TryEnter()!;
        lifetime.Dispose(); lifetime.Dispose();
        Assert.True(lifetime.Token.IsCancellationRequested);
        Assert.False(lifetime.Completion.IsCompleted);
        Assert.Null(lifetime.TryEnter());
        lease.Dispose(); lease.Dispose();
        await lifetime.Completion.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Theory] [InlineData("../outside")] [InlineData("abc & calc")] [InlineData("C:\\file")]
    public void CapturePathsAndCommandsRejectNonIdentifiers(string value)
    {
        Assert.Throws<ArgumentException>(() => ClaudeConnector.FeedPath(value));
        Assert.Throws<ArgumentException>(() => ClaudeConnector.StatusLineCommand(value));
        Assert.Null(ClaudeConnector.CaptureStatusLine(value, "{}"));
    }

    [Fact] public async Task AddingClaudeDoesNotModifyProviderSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "AIPulseTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var provider = Directory.CreateDirectory(Path.Combine(root, "claude")).FullName;
        var settings = Path.Combine(provider, "settings.json");
        const string original = "{\"theme\":\"dark\",\"statusLine\":{\"command\":\"custom\"}}";
        File.WriteAllText(settings, original);
        try
        {
            using var service = new HubService(new LocalStore(Path.Combine(root, "hub")));
            service.AddClaudeContext(provider, refresh: false);
            Assert.Equal(original, File.ReadAllText(settings));
            service.Dispose();
            await service.Stopped.WaitAsync(TimeSpan.FromSeconds(1));
            await service.RefreshAllAsync(true); // No new work after disposal.
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact] public void InvalidSavedPreferencesNormalizeToSupportedValues()
    {
        var settings = new HubSettings { RefreshMinutes = -1, NotchAutoHideMinutes = int.MaxValue, Theme = "invalid", DisplayId = "" };
        settings.Normalize();
        Assert.Equal(1, settings.RefreshMinutes);
        Assert.Equal(5, settings.NotchAutoHideMinutes);
        Assert.Equal("System", settings.Theme);
        Assert.Equal("primary", settings.DisplayId);
    }

    [Fact] public void CorruptSnapshotDoesNotPreventLoadingAccounts()
    {
        var root = Path.Combine(Path.GetTempPath(), "AIPulseTest", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(root);
            var context = new CodexContext { HomePath = root, Name = "Keep me" };
            store.SaveContext(context);
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(root, "hub.db")};Pooling=False"))
            {
                db.Open(); using var cmd = db.CreateCommand();
                cmd.CommandText = "INSERT INTO snapshots VALUES($id, 'invalid json', '2026-01-01')";
                cmd.Parameters.AddWithValue("$id", context.Id); cmd.ExecuteNonQuery();
            }
            var loaded = Assert.Single(store.LoadContexts());
            Assert.Equal("Keep me", loaded.Name);
            Assert.Null(loaded.Snapshot);
        }
        finally { Directory.Delete(root, true); }
    }
}
