using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Sandboxes.Tests.Infrastructure;

internal sealed class CapturingLogger(LogCapture capture, string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        capture.Add(category, logLevel, formatter(state, exception));
}
