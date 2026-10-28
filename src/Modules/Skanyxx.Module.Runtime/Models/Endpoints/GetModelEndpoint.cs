using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Runtime.Models.Features;

namespace Skanyxx.Module.Runtime.Models.Endpoints;

internal sealed class GetModelEndpoint(IMediator mediator) : Endpoint<ModelRef>
{
    public override void Configure()
    {
        Get("{namespace}/{name}");
        Group<ModelsGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
    }

    public override async Task HandleAsync(ModelRef req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new GetModelQuery(req.Namespace, req.Name), ct)).ToHttp());
}
