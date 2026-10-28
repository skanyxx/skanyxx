using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Runtime.Models.Features;

namespace Skanyxx.Module.Runtime.Models.Endpoints;

internal sealed class ListModelsEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/" + ModelsGroup.Prefix);
        ModelsGroup.Apply(Definition);
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ListModelsQuery(Caller.IsOwner(User)), ct)).ToHttp());
}
