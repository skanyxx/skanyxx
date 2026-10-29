using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Deploy;

/// <summary>
/// Where merged agents are on their way to kagent (D363): from the Argo CD Application when <c>Studio:Argo</c> is set,
/// else from the reconciler and kagent. The owner's emergency stop (D121) outranks both. A source that does not answer
/// is <see cref="DeployState.Unknown"/>, never an error for the page. The Argo token rides in the client's default
/// header, never in a URL or a log line.
/// </summary>
internal sealed class DeployStatusReader(
    IHttpClientFactory clients, KAgentApiClient kagent, StudioReconciler reconciler, ReconcileStatus reconcileStatus, IOptions<StudioOptions> options,
    ILogger<DeployStatusReader> logger)
{
    public const string ArgoClientName = "studio-argo";

    private const int MaxJsonBytes = 4 * 1024 * 1024;

    public async Task<IReadOnlyDictionary<string, DeployStatus>> ReadAsync(IReadOnlyCollection<string> agents, CancellationToken ct)
    {
        var result = new Dictionary<string, DeployStatus>();
        if (agents.Count == 0)
            return result;
        var argo = options.Value.Argo;
        var ns = options.Value.Namespace;
        try
        {
            if (argo.Configured)
            {
                var application = await ArgoApplicationAsync(argo, ct);
                foreach (var agent in agents)
                    result[agent] = ArgoStatusMap.For(application, agent, ns);
            }
            else
            {
                var live = (await kagent.GetAgentsAsync(ct)).Where(a => a.Namespace == ns).ToDictionary(a => a.Name);
                foreach (var agent in agents)
                    result[agent] = KAgentStatusMap.For(live.GetValueOrDefault(agent), reconcileStatus.Last);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or System.Text.Json.JsonException)
        {
            logger.LogWarning("Studio deploy status: {Source} did not answer ({Error})", argo.Configured ? "Argo CD" : "kagent", ex.GetType().Name);
            var source = argo.Configured ? ArgoStatusMap.Source : KAgentStatusMap.Source;
            foreach (var agent in agents)
                result[agent] = new DeployStatus(DeployState.Unknown, source,
                    argo.Configured ? "Argo CD cannot be reached right now." : "kagent cannot be reached right now.");
        }
        foreach (var agent in agents)
            await OwnerStopAsync(agent, result, ct);
        return result;
    }

    /// <summary>The owner's emergency stop outranks the source; an agent whose principal cannot be read keeps the source's status alone.</summary>
    private async Task OwnerStopAsync(string agent, Dictionary<string, DeployStatus> result, CancellationToken ct)
    {
        try
        {
            if ((await reconciler.PrincipalAsync(agent, ct)).Suspended)
                result[agent] = new DeployStatus(DeployState.Suspended, result[agent].Source, "The owner suspended it; it is not running until resumed.");
        }
        catch (Exception ex) when (ex is StudioApplyException or HttpRequestException or TimeoutException)
        {
            logger.LogWarning("Studio deploy status: whether {Agent} is suspended could not be read ({Error})", agent, ex.GetType().Name);
        }
    }

    private async Task<JsonNode?> ArgoApplicationAsync(ArgoOptions argo, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(argo.TimeoutSeconds));
        try
        {
            using var response = await clients.CreateClient(ArgoClientName)
                .GetAsync($"api/v1/applications/{Uri.EscapeDataString(argo.Application)}", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Argo CD answered HTTP {(int)response.StatusCode}", null, response.StatusCode);
            if (response.Content.Headers.ContentLength > MaxJsonBytes)
                throw new HttpRequestException("Argo CD answered more than the studio reads", null, HttpStatusCode.BadGateway);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, deadline.Token)) > 0)
            {
                if (buffer.Length + read > MaxJsonBytes)
                    throw new HttpRequestException("Argo CD answered more than the studio reads", null, HttpStatusCode.BadGateway);
                buffer.Write(chunk, 0, read);
            }
            buffer.Position = 0;
            return await JsonNode.ParseAsync(buffer, cancellationToken: deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Argo CD did not answer within {argo.TimeoutSeconds} s.");
        }
    }
}
