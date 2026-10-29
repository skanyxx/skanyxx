using System.Text;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Contracts;
using Skanyxx.Module.Tickets.Features.Runs;

namespace Skanyxx.Module.Tickets.Endpoints.Runs;

internal sealed class GetReportEndpoint(IMediator mediator) : Endpoint<RunRouteRequest>
{
    public override void Configure()
    {
        Get("runs/{id}/report");
        // D394: runs and pipelines hold Jira snapshots, prompts and answers: the Tickets panel is supervisors' (D392).
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(RunRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new GetRunQuery(Caller.UserId(User), req.Id), ct);
        await Send.ResultAsync(outcome.Status == OutcomeStatus.Ok
            ? Results.Text(outcome.Value!.ToMarkdown(), "text/markdown", Encoding.UTF8)
            : outcome.ToHttp(r => r.Id));
    }
}
