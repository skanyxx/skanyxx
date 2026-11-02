using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Features.Runs;
using Skanyxx.Module.Investigate.Reports;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Endpoints;

/// <summary>The report as a JSON download (the whole answers; the PDF cuts very long ones).</summary>
internal sealed class JsonReportEndpoint(IMediator mediator) : Endpoint<IdRequest>
{
    public override void Configure()
    {
        Get("runs/{id}/report.json");
        Group<InvestigateGroup>();
        Roles(InvestigateGroup.Roles);
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct)
    {
        var viewer = Viewer.From(User);
        var outcome = await mediator.Send(new GetRunQuery(viewer, req.Id), ct);
        if (outcome.Status == OutcomeStatus.Ok)
            HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"investigation-{req.Id:N}.json\"";
        await Send.ResultAsync(outcome.ToHttp(r => RunReport.From(r, viewer)));
    }
}
