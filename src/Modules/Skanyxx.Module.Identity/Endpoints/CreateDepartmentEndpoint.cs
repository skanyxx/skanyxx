using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class CreateDepartmentEndpoint(IMediator mediator) : Endpoint<DepartmentRequest>
{
    public override void Configure()
    {
        Post("org/departments");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(DepartmentRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(
            new CreateDepartmentCommand(Caller.UserId(User)!, req.Slug ?? "", req.Name?.Trim() ?? ""), ct)).ToHttp(d => d));
}
