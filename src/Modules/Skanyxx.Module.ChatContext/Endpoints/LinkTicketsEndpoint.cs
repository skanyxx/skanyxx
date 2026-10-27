using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.ChatContext.Features;

namespace Skanyxx.Module.ChatContext.Endpoints;

internal sealed class LinkTicketsEndpoint(IMediator mediator) : Endpoint<LinkTicketsRequest>
{
    public override void Configure()
    {
        Put("sessions/{id}/tickets");
        Group<ChatIndexGroup>();
    }

    public override async Task HandleAsync(LinkTicketsRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new LinkTicketsCommand(ChatReader.From(User), req.Id, req.TicketKeys ?? []), ct)).ToHttp());
}
