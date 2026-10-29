using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Runtime.Models.Features;

/// <summary>
/// Every model write in the identity audit, as tool server and agent writes are (D255, D325): the intent before kagent is
/// called (no row, no write) and the outcome after. Names and flags only: provider, model, field names, endpoint
/// origins, whether the key changed. Never the key.
/// </summary>
internal static class ModelAudit
{
    public const string CreateRequested = "model.create_requested";
    public const string UpdateRequested = "model.update_requested";
    public const string DeleteRequested = "model.delete_requested";
    public const string Created = "model.created";
    public const string Updated = "model.updated";
    public const string Deleted = "model.deleted";
    public const string Failed = "model.write_failed";

    /// <summary>An intent: a failure propagates, before anything changed (fail closed).</summary>
    public static Task WriteAsync(IMediator mediator, string action, string actorId, string ns, string name, object details) =>
        mediator.Send(new RecordAuditCommand(action, actorId, $"{ns}/{name}", details), CancellationToken.None);

    /// <summary>After kagent answered: the intent row already holds the attempt, so a failing row here is logged, not thrown.</summary>
    public static async Task OutcomeAsync(IMediator mediator, ILogger logger, string action, string actorId, string ns, string name, object details)
    {
        try
        {
            await WriteAsync(mediator, action, actorId, ns, name, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Model {Namespace}/{Name}: {Action} by {ActorUserId} was not written to the audit; its intent row stands", ns, name, action, actorId);
        }
    }
}
