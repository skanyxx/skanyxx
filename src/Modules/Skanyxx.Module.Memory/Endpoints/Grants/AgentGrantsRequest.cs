using FastEndpoints;
using Skanyxx.Module.Memory.Features.Grants;

namespace Skanyxx.Module.Memory.Endpoints.Grants;

public sealed class AgentGrantsRequest
{
    /// <summary>Route only: a <c>?agentId=</c> must not retarget the request (see CardRouteRequest).</summary>
    [DontBind(Source.QueryParam)]
    public string AgentId { get; set; } = "";

    /// <summary>PUT body only.</summary>
    [DontBind(Source.QueryParam)]
    public List<GrantEntry> Grants { get; set; } = [];
}
