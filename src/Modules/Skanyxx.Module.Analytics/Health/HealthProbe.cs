using Skanyxx.Core.Platform.Analytics;
using System.Diagnostics;
using System.Text.Json.Nodes;
using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Skanyxx.Core.Platform.Runtime;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Analytics.Health;

/// <summary>
/// Reads every dependency at once: kagent (ping with its latency, agents, tool servers, ModelConfigs, the default model)
/// and the health checks other modules register. kagent's calls have their own deadlines, the checks the Host's
/// health timeout, so the Dashboard waits at most one of each. A failure is a null fact, never its message.
/// </summary>
internal sealed class HealthProbe(KAgentApiClient kagent, HealthCheckService health, IMediator mediator)
{
    public async Task<HealthFacts> ReadAsync(CancellationToken ct)
    {
        var checks = ChecksAsync(ct);
        var latency = await Quietly(async () =>
        {
            var clock = Stopwatch.StartNew();
            await kagent.PingAsync(ct);
            return (long?)clock.ElapsedMilliseconds;
        });
        if (latency is null)
            return new HealthFacts(null, null, null, null, null, await checks);

        var agents = Quietly(async () =>
        {
            var active = (await kagent.GetAgentsAsync(ct)).Where(a => !a.Suspended).ToList();
            return ((int, int)?)(active.Count(a => a.Ready), active.Count);
        });
        var servers = Quietly(async () =>
        {
            var list = (await kagent.GetToolServerListAsync(ct)).OfType<JsonObject>().ToList();
            return ((int, int)?)(list.Count(s => s["discoveredTools"] is JsonArray { Count: > 0 }), list.Count);
        });
        var models = Quietly(async () => (int?)(await kagent.GetModelConfigRefsAsync(ct)).Count);
        var configured = Quietly(async () => (await mediator.Send(new ModelSettingsQuery(), ct)).Value?.Configured);
        await Task.WhenAll(agents, servers, models, configured, checks);
        return new HealthFacts(latency, agents.Result, servers.Result, models.Result, configured.Result, checks.Result);
    }

    private async Task<IReadOnlyDictionary<string, CheckFact>> ChecksAsync(CancellationToken ct)
    {
        var report = await health.CheckHealthAsync(r =>
            r.Name.EndsWith(DashboardChecks.PostgresSuffix, StringComparison.Ordinal)
            || r.Name is DashboardChecks.AnswerCache or DashboardChecks.StudioGit or DashboardChecks.StudioReconciler or DashboardChecks.Sandboxes, ct);
        return report.Entries.ToDictionary(e => e.Key, e => new CheckFact(e.Value.Status, e.Value.Data));
    }

    private static async Task<T?> Quietly<T>(Func<Task<T?>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            return default;
        }
    }
}
