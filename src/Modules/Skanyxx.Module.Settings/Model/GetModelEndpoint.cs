using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;

namespace Skanyxx.Module.Settings.Model;

/// <summary>The owner's model step as an API (the <c>/Model</c> page sends the same commands). Owner only (roles.md).</summary>
internal sealed class GetModelEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get(ModelRoutes.Path);
        Roles(SkanyxxRoles.Owner);
        Options(b => b.WithMetadata(new DisableCorsAttribute()));
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ModelSettingsQuery(), ct)).ToHttp(s => s));
}
