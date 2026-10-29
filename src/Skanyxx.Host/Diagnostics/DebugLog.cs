using Serilog.Core;
using Serilog.Events;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Diagnostics;

/// <summary>
/// The Debug page's view of the log since this Host started (D353): the last <see cref="MaxLines"/> warnings and
/// errors, and the last <see cref="MaxRequests"/> request timings from Serilog's request log. In memory only, per
/// replica, gone at restart. Messages pass through <see cref="SecretText.Redact"/> before they are kept.
/// </summary>
public sealed class DebugLog : ILogEventSink
{
    public const int MaxLines = 100;
    public const int MaxRequests = 2000;

    private readonly Lock _gate = new();
    private readonly Queue<LogLine> _lines = new();
    private readonly Queue<RequestSample> _requests = new();

    public void Emit(LogEvent logEvent)
    {
        if (Request(logEvent) is { } sample)
        {
            Add(_requests, sample, MaxRequests);
            return;
        }
        if (logEvent.Level < LogEventLevel.Warning)
            return;
        var source = logEvent.Properties.TryGetValue("SourceContext", out var context) ? context.ToString().Trim('"') : "";
        Add(_lines, new LogLine(logEvent.Timestamp, logEvent.Level.ToString(), source, SecretText.Redact(logEvent.RenderMessage()),
            logEvent.Exception is { } ex ? $"{ex.GetType().Name}: {SecretText.Redact(ex.Message)}" : null), MaxLines);
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<LogLine> Lines()
    {
        lock (_gate)
            return [.. _lines.Reverse()];
    }

    /// <summary>Per method and path, slowest p95 first.</summary>
    public IReadOnlyList<RequestTiming> Timings(int top)
    {
        RequestSample[] samples;
        lock (_gate)
            samples = [.. _requests];
        return [.. samples.GroupBy(s => (s.Method, s.Path))
            .Select(g =>
            {
                var ms = g.Select(s => s.Milliseconds).Order().ToArray();
                return new RequestTiming(g.Key.Method, g.Key.Path, ms.Length, Percentile(ms, 0.5), Percentile(ms, 0.95), ms[^1],
                    g.Count(s => s.Status >= 500));
            })
            .OrderByDescending(t => t.P95Ms).Take(top)];
    }

    public int RequestCount
    {
        get
        {
            lock (_gate)
                return _requests.Count;
        }
    }

    private void Add<T>(Queue<T> queue, T item, int max)
    {
        lock (_gate)
        {
            queue.Enqueue(item);
            while (queue.Count > max)
                queue.Dequeue();
        }
    }

    private static double Percentile(double[] sorted, double p) => sorted[(int)Math.Ceiling(p * sorted.Length) - 1];

    // Serilog.AspNetCore's request log: "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms".
    private static RequestSample? Request(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("RequestMethod", out var method)
        && logEvent.Properties.TryGetValue("RequestPath", out var path)
        && logEvent.Properties.TryGetValue("StatusCode", out var status) && status is ScalarValue { Value: int code }
        && logEvent.Properties.TryGetValue("Elapsed", out var elapsed) && elapsed is ScalarValue { Value: double ms }
            ? new RequestSample(method.ToString().Trim('"'), path.ToString().Trim('"'), code, ms)
            : null;
}
