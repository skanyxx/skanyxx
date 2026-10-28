using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Runtime.Models.Features;

namespace Skanyxx.Module.Runtime.Models.Endpoints;

/// <summary><c>409 default</c> for the default model; <c>409 in_use</c> with the agents using it, until <c>?confirm=true</c>.</summary>
internal sealed class DeleteModelEndpoint(IMediator mediator) : Endpoint<DeleteModelRequest>
{
    public override void Configure()
    {
        Delete("{namespace}/{name}");
        Group<ModelsGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(DeleteModelRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new DeleteModelConfigCommand(Caller.UserId(User)!, req.Namespace, req.Name, req.Confirm), ct)).ToHttp());
}
