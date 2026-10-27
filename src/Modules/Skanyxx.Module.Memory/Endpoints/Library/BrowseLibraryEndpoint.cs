using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Endpoints.Library;

/// <summary>The library list over REST (requirements.md §3.7): the same query the /Library page sends, with its filters and paging.</summary>
internal sealed class BrowseLibraryEndpoint(IMediator mediator) : Endpoint<BrowseLibraryRequest>
{
    public override void Configure()
    {
        Get("library");
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(BrowseLibraryRequest req, CancellationToken ct)
    {
        var filter = new LibraryFilter(req.Ticket, req.Keyword, req.Type, req.Origin, req.Agent, req.Offset);
        var outcome = await mediator.Send(new BrowseLibraryQuery(LibraryUser.From(User), req.Q, req.Scope, filter), ct);
        await Send.ResultAsync(outcome.ToHttp());
    }
}
