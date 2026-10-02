using FastEndpoints;

namespace Skanyxx.Module.Memory.Endpoints.AgentSecrets;

public sealed class AgentSecretRequest
{
    /// <summary>Route only: a <c>?agentId=</c> must not retarget the request (see CardRouteRequest).</summary>
    [DontBind(Source.QueryParam)]
    public string AgentId { get; set; } = "";
}
