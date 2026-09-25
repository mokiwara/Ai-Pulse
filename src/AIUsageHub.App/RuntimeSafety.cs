using System.Diagnostics;
using System.Text;

namespace AIUsageHub;

public static class BoundedText
{
    public static async Task<string> ReadAsync(TextReader reader, int limit, CancellationToken token)
    {
        var result = new StringBuilder(Math.Min(limit, 4096));
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit - result.Length + 1)), token)) != 0)
        {
            if (result.Length + count > limit) throw new InvalidDataException("Provider output exceeded its limit.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }

    public static async Task DrainAsync(TextReader reader, CancellationToken token)
    {
        var buffer = new char[4096];
        try { while (await reader.ReadAsync(buffer.AsMemory(), token) != 0) { } }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public static async Task<string> ReadFileAsync(string path, int limit, CancellationToken token)
    {
        using var reader = new StreamReader(path);
        return await ReadAsync(reader, limit, token);
    }
}

// Keep a small read-ahead buffer between JSON-RPC responses, without unbounded ReadLine allocations.
public sealed class BoundedLineReader(TextReader reader, int limit)
{
    private readonly char[] _buffer = new char[4096];
    private int _position, _length;
    public async Task<string?> ReadLineAsync(CancellationToken token)
    {
        var line = new StringBuilder();
        while (true)
        {
            if (_position == _length)
            {
                _length = await reader.ReadAsync(_buffer.AsMemory(), token);
                _position = 0;
                if (_length == 0) return line.Length == 0 ? null : line.ToString().TrimEnd('\r');
            }
            var ch = _buffer[_position++];
            if (ch == '\n') return line.ToString().TrimEnd('\r');
            if (line.Length >= limit) throw new InvalidDataException("Provider line exceeded its limit.");
            line.Append(ch);
        }
    }
}

public static class ChildProcess
{
    public static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (TimeoutException) { }
    }
}

// Cancellation resources remain alive until every operation has unwound its finally blocks.
public sealed class AsyncLifetime : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _source = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active = 1;
    private bool _stopping;
    public CancellationToken Token { get; }
    public Task Completion => _completion.Task;
    public AsyncLifetime() => Token = _source.Token;
    public IDisposable? TryEnter()
    {
        lock (_gate)
        {
            if (_stopping) return null;
            _active++;
            return new Lease(this);
        }
    }
    private void Release()
    {
        lock (_gate)
        {
            if (--_active != 0) return;
            _source.Dispose();
            _completion.TrySetResult();
        }
    }
    public void Dispose()
    {
        lock (_gate) { if (_stopping) return; _stopping = true; }
        try { _source.Cancel(); }
        finally { Release(); }
    }
    private sealed class Lease(AsyncLifetime owner) : IDisposable
    {
        private AsyncLifetime? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
