using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Identity.Tests.Infrastructure;

/// <summary>Every line the app logs, at every level, with its exception, for asserting what never appears in a log.</summary>
public sealed class AllLog : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => [.. _lines];

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _lines.Enqueue($"{logLevel}: {formatter(state, exception)} {exception}");

    public void Dispose() { }
}
