using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;

namespace Skanyxx.Module.Memory.Mcp;

/// <summary>
/// Drops endpoints carrying <see cref="McpPortMetadata"/> unless the connection arrived on that port, so they answer
/// 404 on every other listener. It matches the socket's local port, not the Host header: <c>RequireHost("*:port")</c>
/// would let any caller of the public port reach /mcp by sending <c>Host: x:port</c>.
/// </summary>
internal sealed class McpPortMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy
{
    public override int Order => 0;

    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints) =>
        endpoints.Any(e => e.Metadata.GetMetadata<McpPortMetadata>() is not null);

    public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].Endpoint?.Metadata.GetMetadata<McpPortMetadata>() is { } only && only.Port != httpContext.Connection.LocalPort)
                candidates.SetValidity(i, false);
        }
        return Task.CompletedTask;
    }
}
