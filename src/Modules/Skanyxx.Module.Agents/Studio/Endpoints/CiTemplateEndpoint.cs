using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Ci;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>The agent repo's CI workflow (D362), as text to copy: builders and supervisors.</summary>
internal sealed class CiTemplateEndpoint : EndpointWithoutRequest
{
    public override void Configure() => Get("api/studio/ci-template");

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!StudioUser.From(User).CanEnter)
        {
            await Send.ResultAsync(Results.Problem("Only builders and supervisors use the studio.", statusCode: StatusCodes.Status403Forbidden));
            return;
        }
        await Send.ResultAsync(Results.Text(CiTemplate.Text, "text/yaml; charset=utf-8"));
    }
}
