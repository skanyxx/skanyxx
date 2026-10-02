using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Sandboxes.Tests.Infrastructure;

/// <summary>Every log entry the app writes, for asserting what was (not) logged.</summary>
public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<(string Category, LogLevel Level, string Message)> _entries = new();

    public IReadOnlyList<(string Category, LogLevel Level, string Message)> Entries => [.. _entries];

    /// <summary>Warnings or worse, from any category (Skanyxx's own code and ASP.NET's).</summary>
    public IReadOnlyList<string> Warnings =>
        [.. _entries.Where(e => e.Level >= LogLevel.Warning).Select(e => $"{e.Category}: {e.Message}")];

    /// <summary>Waits until the host logged the end of a request whose line contains <paramref name="path"/>.</summary>
    public async Task WaitForRequestFinishedAsync(string path)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!_entries.Any(e => e.Message.StartsWith("Request finished") && e.Message.Contains(path)))
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"No 'Request finished' for {path}.");
            await Task.Delay(20);
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    internal void Add(string category, LogLevel level, string message) => _entries.Enqueue((category, level, message));
}
