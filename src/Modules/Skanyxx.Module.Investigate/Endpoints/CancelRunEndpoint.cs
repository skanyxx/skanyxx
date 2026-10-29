using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Features.Runs;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Endpoints;

internal sealed class CancelRunEndpoint(IMediator mediator) : Endpoint<IdRequest>
{
    public override void Configure()
    {
        Post("runs/{id}/cancel");
        Group<InvestigateGroup>();
        Roles(InvestigateGroup.Roles);
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new CancelRunCommand(Viewer.From(User), req.Id), ct)).ToHttp());
}
