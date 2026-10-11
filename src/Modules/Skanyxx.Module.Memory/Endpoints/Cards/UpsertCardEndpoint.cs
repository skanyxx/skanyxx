using Skanyxx.Core.Platform;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MediatR;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Features.Cards;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

internal sealed class UpsertCardEndpoint(IMediator mediator) : Endpoint<UpsertCardRequest>
{
    public override void Configure()
    {
        Put("cards/{scope}/{key}");
        Group<MemoryGroup>();
        // A body bound for the object store may be larger than the group's cap (D180); JSON can escape every byte as
        // \uXXXX. Decided per request from this app's options, before the body is read: FastEndpoints' Config in
        // Configure() resolves through a process-wide static, so it can hand one app another app's settings.
        Options(b => b.AddEndpointFilter(RaiseCapForTheObjectStore));
    }

    private static ValueTask<object?> RaiseCapForTheObjectStore(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var store = http.RequestServices.GetRequiredService<IOptions<MemoryOptions>>().Value.ObjectStore;
        if (store.Enabled)
            http.Features.GetRequiredFeature<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize =
                MemoryGroup.MaxBodyBytes + 6L * store.MaxBodyBytes;
        return next(context);
    }

    public override async Task HandleAsync(UpsertCardRequest req, CancellationToken ct)
    {
        var command = new UpsertCardCommand(
            HumanCaller.From(User), req.Scope, req.Key, req.Version, req.Type, req.What, req.Why, req.Body, req.Source);
        var outcome = await mediator.Send(command, ct);
        await Send.ResultAsync(outcome.ToHttp(CardMapper.ToDto));
    }
}
