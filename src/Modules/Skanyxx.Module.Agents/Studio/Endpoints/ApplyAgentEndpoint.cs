using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>Supervisor or owner (D366): open and merge at once. <c>403</c> for builders and employees, who propose.</summary>
internal sealed class ApplyAgentEndpoint(IMediator mediator) : Endpoint<AgentDraft>
{
    public override void Configure()
    {
        Post("api/studio/apply");
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
        Options(b => b.WithMetadata(new RequestSizeLimitAttribute(64 * 1024)));
    }

    public override async Task HandleAsync(AgentDraft req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ApplyAgentCommand(StudioUser.From(User), req.Normalized()), ct)).ToHttp());
}
