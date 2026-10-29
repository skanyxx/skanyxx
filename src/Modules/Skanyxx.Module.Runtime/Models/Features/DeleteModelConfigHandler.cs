using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;
using Skanyxx.Module.Runtime.Models.Definition;

namespace Skanyxx.Module.Runtime.Models.Features;

/// <summary>
/// The default model is never deleted (the seed agents and Chat run on it). A model agents still use is deleted only once
/// the owner has seen which (<c>409 in_use</c> with the list first): kagent deletes it either way, and those agents stop.
/// The delete is in the audit (<see cref="ModelAudit"/>) with the agents that used the model.
/// </summary>
internal sealed class DeleteModelConfigHandler(KAgentApiClient kagent, IMediator mediator, ILogger<DeleteModelConfigHandler> logger)
    : IRequestHandler<DeleteModelConfigCommand, Outcome<ModelDeletion>>
{
    public async Task<Outcome<ModelDeletion>> Handle(DeleteModelConfigCommand command, CancellationToken ct)
    {
        var configRef = $"{command.Namespace}/{command.Name}";
        if (!ModelRefs.IsKubeName(command.Namespace) || !ModelRefs.IsKubeName(command.Name))
            return Outcome<ModelDeletion>.NotFound(ModelFailure.NotFound);
        if (configRef == kagent.ModelConfigRef)
            return Outcome<ModelDeletion>.Conflict(new ModelDeletion(command.Namespace, command.Name, []), ModelFailure.IsDefault(configRef), "default");
        object? details = null;
        try
        {
            if (await kagent.GetModelConfigAsync(command.Namespace, command.Name, ct) is null)
                return Outcome<ModelDeletion>.NotFound(ModelFailure.NotFound);
            var users = (await kagent.GetAgentsAsync(ct))
                .Where(a => a.ModelConfig is not null && $"{a.Namespace}/{a.ModelConfig}" == configRef)
                .Select(a => $"{a.Namespace}/{a.Name}").Order(StringComparer.Ordinal).ToList();
            var deletion = new ModelDeletion(command.Namespace, command.Name, users);
            if (users.Count > 0 && !command.InUseConfirmed)
                return Outcome<ModelDeletion>.Conflict(deletion, ModelFailure.InUse(users), "in_use");
            details = new { @namespace = command.Namespace, name = command.Name, agentsUsing = users, inUseConfirmed = command.InUseConfirmed };
            await ModelAudit.WriteAsync(mediator, ModelAudit.DeleteRequested, command.ActorId, command.Namespace, command.Name, details);
            // From the intent row on, the person leaving does not cancel the delete half way.
            if (!await kagent.DeleteModelConfigAsync(command.Namespace, command.Name, CancellationToken.None))
            {
                await ModelAudit.OutcomeAsync(mediator, logger, ModelAudit.Failed, command.ActorId, command.Namespace, command.Name, new { details, reason = "not found" });
                return Outcome<ModelDeletion>.NotFound(ModelFailure.NotFound);
            }
            await ModelAudit.OutcomeAsync(mediator, logger, ModelAudit.Deleted, command.ActorId, command.Namespace, command.Name, details);
            logger.LogWarning("Model {ModelConfig} deleted by {ActorUserId}; agents using it: {AgentCount}", configRef, command.ActorId, users.Count);
            return Outcome<ModelDeletion>.Ok(deletion);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Model {ModelConfig} not deleted for {ActorUserId}", configRef, command.ActorId);
            if (details is not null)
                await ModelAudit.OutcomeAsync(mediator, logger, ModelAudit.Failed, command.ActorId, command.Namespace, command.Name, new { details, reason = "delete" });
            return Outcome<ModelDeletion>.Unavailable(ModelFailure.Unavailable);
        }
    }
}
