using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Identity.Tests.Infrastructure;

/// <summary>Collects every Warning the app logs, from any category, for asserting what was logged.</summary>
public sealed class WarningLog : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<string> _warnings = new();

    public IReadOnlyList<string> Warnings => [.. _warnings];

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
            _warnings.Enqueue(formatter(state, exception));
    }

    public void Dispose() { }
}
