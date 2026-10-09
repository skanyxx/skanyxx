using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Chat.Features;

namespace Skanyxx.Module.Chat.Endpoints;

/// <summary>The agents a signed-in person may talk to (merged only, D4).</summary>
internal sealed class ChatAgentsEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("api/chat/agents");
        Options(b => b.WithMetadata(new DisableCorsAttribute()));
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ListChatAgentsQuery(), ct)).ToHttp(a => a));
}
