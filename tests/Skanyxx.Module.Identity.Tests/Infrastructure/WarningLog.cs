using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Identity.Tests.Infrastructure;

/// <summary>Collects every Warning and Error the app logs, from any category, for asserting what was logged.</summary>
public sealed class WarningLog : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<string> _warnings = new();
    private readonly ConcurrentQueue<string> _errors = new();

    public IReadOnlyList<string> Warnings => [.. _warnings];

    public IReadOnlyList<string> Errors => [.. _errors];

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
            _warnings.Enqueue(formatter(state, exception));
        else if (logLevel == LogLevel.Error)
            _errors.Enqueue(formatter(state, exception));
    }

    public void Dispose() { }
}
