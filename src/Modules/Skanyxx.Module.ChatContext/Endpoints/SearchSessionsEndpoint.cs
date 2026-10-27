using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.ChatContext.Features;

namespace Skanyxx.Module.ChatContext.Endpoints;

internal sealed class SearchSessionsEndpoint(IMediator mediator) : Endpoint<SearchSessionsRequest>
{
    public override void Configure()
    {
        Get("sessions");
        Group<ChatIndexGroup>();
    }

    public override async Task HandleAsync(SearchSessionsRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new SearchSessionsQuery(ChatReader.From(User), req.ToFilter()), ct)).ToHttp());
}
