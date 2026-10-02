using FastEndpoints;

namespace Skanyxx.Module.Memory.Endpoints.AgentSecrets;

public sealed class IssueAgentSecretRequest
{
    /// <summary>Route only: a <c>?agentId=</c> must not retarget the request (see CardRouteRequest).</summary>
    [DontBind(Source.QueryParam)]
    public string AgentId { get; set; } = "";

    /// <summary>Optional body field; off when absent. Only the owner may turn it on (D084).</summary>
    [DontBind(Source.QueryParam)]
    public bool ActsForUsers { get; set; }
}
