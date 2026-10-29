using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Features.Runs;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Endpoints;

internal sealed class GetRunEndpoint(IMediator mediator) : Endpoint<IdRequest>
{
    public override void Configure()
    {
        Get("runs/{id}");
        Group<InvestigateGroup>();
        Roles(InvestigateGroup.Roles);
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct)
    {
        var viewer = Viewer.From(User);
        await Send.ResultAsync((await mediator.Send(new GetRunQuery(viewer, req.Id), ct)).ToHttp(r => RunMapper.Full(r, viewer)));
    }
}
