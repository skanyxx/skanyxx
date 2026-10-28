using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.ToolServers.Data;

namespace Skanyxx.Module.ToolServers.Features;

/// <summary>
/// Every tool server write in the identity audit, as agent writes are (D255): the intent before kagent is called (no
/// row, no write) and the outcome after. Names only: the URL's origin, header names and Secret names, never a value.
/// </summary>
internal static class ToolServerAudit
{
    public const string CreateRequested = "toolserver.create_requested";
    public const string UpdateRequested = "toolserver.update_requested";
    public const string DeleteRequested = "toolserver.delete_requested";
    public const string Created = "toolserver.created";
    public const string Updated = "toolserver.updated";
    public const string Deleted = "toolserver.deleted";
    public const string Failed = "toolserver.write_failed";

    public static Task WriteAsync(IMediator mediator, string action, string? userId, string ns, string name, ToolServerRecord? record = null, string? reason = null) =>
        mediator.Send(new RecordAuditCommand(action, userId, $"{ns}/{name}", new
        {
            @namespace = ns,
            name,
            address = record is null ? null : ToolServerCatalog.Origin(new Uri(record.Url)),
            protocol = record?.Protocol,
            headers = record?.Headers.Select(h => new { h.Name, h.SecretName, h.SecretKey }).ToList(),
            reason
        }), CancellationToken.None);

    public static async Task OutcomeAsync(IMediator mediator, ILogger logger, string action, string? userId, string ns, string name,
        ToolServerRecord? record = null, string? reason = null)
    {
        try
        {
            await WriteAsync(mediator, action, userId, ns, name, record, reason);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool server {Namespace}/{Name}: {Action} by {ActorUserId} was not written to the audit; its intent row stands", ns, name, action, userId);
        }
    }
}
