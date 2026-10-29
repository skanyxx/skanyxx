using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>The YAML a form would commit and what stops it (D361); changes nothing.</summary>
internal sealed class ManifestPreviewEndpoint(IMediator mediator) : Endpoint<AgentDraft>
{
    public override void Configure()
    {
        Post("api/studio/manifest");
        Roles(StudioRoles.People);
        Options(b => b.WithMetadata(new RequestSizeLimitAttribute(64 * 1024)));
    }

    public override async Task HandleAsync(AgentDraft req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ManifestPreviewQuery(StudioUser.From(User), req), ct)).ToHttp());
}
