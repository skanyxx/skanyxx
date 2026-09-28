using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Memory.Tests.Infrastructure;

/// <summary>Every log entry at every level, with its structured values and exception, so a test can prove a value never reaches a log.</summary>
public sealed class LogCapture : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<string> _entries = new();

    public string All => string.Join('\n', _entries);

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        _entries.Enqueue($"scope: {state}");
        return null;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(' ', pairs.Select(p => $"{p.Key}={p.Value}")) : "";
        _entries.Enqueue($"{logLevel} {formatter(state, exception)} {values} {exception}");
    }

    public void Dispose() { }
}
