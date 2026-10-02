using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Tickets.Tests.Infrastructure;

/// <summary>Collects Error-level log entries from the app under test.</summary>
public sealed class ErrorLog : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyList<string> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
            _entries.Enqueue($"{formatter(state, exception)} {exception?.GetType().Name}");
    }

    public void Dispose() { }
}
