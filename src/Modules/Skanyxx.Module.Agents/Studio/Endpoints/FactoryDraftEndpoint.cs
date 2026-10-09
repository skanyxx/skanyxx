using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>Builder-only factory (D022): words in, a draft of the form out. Opens no PR.</summary>
internal sealed class FactoryDraftEndpoint(IMediator mediator) : Endpoint<FactoryRequest>
{
    public override void Configure()
    {
        Post("api/studio/factory");
        Options(b => b.WithMetadata(new RequestSizeLimitAttribute(16 * 1024)));
    }

    public override async Task HandleAsync(FactoryRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new FactoryDraftCommand(StudioUser.From(User), req.Request?.Trim() ?? ""), ct)).ToHttp());
}
