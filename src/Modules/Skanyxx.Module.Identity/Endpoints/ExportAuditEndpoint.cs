using System.Text;
using System.Text.Json;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Features.AuditExplorer;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>
/// The query's hits as a CSV or JSON download, the owner's only (D152), redacted, at most
/// <see cref="ExportAuditCommand.MaxRows"/> rows: a cut export says so in <c>X-Audit-Total</c>/<c>X-Audit-Truncated</c>
/// and, for JSON, in its body. Never cached.
/// </summary>
internal sealed class ExportAuditEndpoint(IMediator mediator, TimeProvider time) : Endpoint<ExportAuditRequest>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public override void Configure()
    {
        Get("audit/export");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(ExportAuditRequest req, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var format = req.Format ?? "csv";
        var export = (await mediator.Send(new ExportAuditCommand(req.Filter(now), req.Sort == "asc", format, Caller.UserId(User)!), ct)).Value!;
        var body = format == "csv"
            ? AuditCsv.Write(export)
            : JsonSerializer.Serialize(new
            {
                exportedAt = now,
                total = export.Total,
                truncated = export.Truncated,
                rows = export.Rows.Select(r => new { r.Id, r.At, r.Action, r.Module, r.Outcome, r.ActorId, actorEmail = export.Email(r.ActorId),
                    r.TargetId, targetEmail = export.Email(r.TargetId), r.RemoteIp, r.Details })
            }, Json);
        HttpContext.Response.Headers.CacheControl = "no-store";
        HttpContext.Response.Headers["X-Audit-Total"] = export.Total.ToString();
        HttpContext.Response.Headers["X-Audit-Truncated"] = export.Truncated ? "true" : "false";
        await Send.BytesAsync(Encoding.UTF8.GetBytes(body), $"audit-{now:yyyyMMdd-HHmmss}.{format}",
            format == "csv" ? "text/csv; charset=utf-8" : "application/json", cancellation: ct);
    }
}
