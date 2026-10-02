using Microsoft.AspNetCore.Routing;

namespace Skanyxx.Core;

/// <summary>A module that maps endpoints the MVC/FastEndpoints discovery cannot see (e.g. MCP).</summary>
public interface IEndpointModule
{
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
