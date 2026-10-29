using System.Net;
using System.Text.Json.Nodes;
using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;
using Skanyxx.Core.Services;
using Skanyxx.Module.Runtime.Models.Definition;

namespace Skanyxx.Module.Runtime.Models.Features;

/// <summary>
/// Creates or edits a ModelConfig over kagent's API (D099, D106). The API key goes inline to kagent, which keeps it in a
/// Secret it owns; Skanyxx never stores, returns or logs it. Who changed what is in the audit (<see cref="ModelAudit"/>)
/// and logged at Warning, like the other security events. kagent 0.10.2's update has no precondition, so an edit is
/// read-modify-write and the last writer wins.
/// </summary>
internal sealed class SaveModelConfigHandler(KAgentApiClient kagent, IMediator mediator, ILogger<SaveModelConfigHandler> logger)
    : IRequestHandler<SaveModelConfigCommand, Outcome<ModelConfigDetail>>
{
    public async Task<Outcome<ModelConfigDetail>> Handle(SaveModelConfigCommand command, CancellationToken ct)
    {
        var configRef = $"{command.Namespace}/{command.Name}";
        var newKey = !string.IsNullOrEmpty(command.ApiKey);
        object? details = null;
        try
        {
            var provider = ProviderChoice.Of(await kagent.GetModelProvidersAsync(ct)).FirstOrDefault(p => p.Type == command.Provider)
                ?? throw ModelFailure.Invalid([$"kagent does not offer the provider '{command.Provider}'."]);
            // The validator refused keys that collide once trimmed.
            var (values, problems) = ModelParams.Check(provider, command.Params.ToDictionary(p => p.Key.Trim(), p => p.Value));
            if (problems.Count > 0)
                throw ModelFailure.Invalid(problems);

            JsonObject saved;
            if (command.Create)
            {
                if (provider.Schema.TakesKey && !newKey && !command.NoApiKey)
                    throw ModelFailure.Invalid([ModelFailure.NoKey(provider.Type)]);
                var spec = ModelConfigSpec.Edit(null, provider.Type, provider.Schema, command.Model, values, dropKey: true);
                details = Details(command, provider, values, spec, baseUrlChanged: ModelConfigSpec.Endpoints(spec, provider.Schema).Any(e => e is not null));
                await ModelAudit.WriteAsync(mediator, ModelAudit.CreateRequested, command.ActorId, command.Namespace, command.Name, details);
                // From the intent row on, the person leaving does not cancel the write half way.
                saved = await kagent.CreateModelConfigAsync(command.Namespace, command.Name, spec, command.ApiKey, CancellationToken.None);
            }
            else
            {
                var current = await kagent.GetModelConfigAsync(command.Namespace, command.Name, ct);
                if (current is null)
                    return Outcome<ModelConfigDetail>.NotFound(ModelFailure.NotFound);
                var currentSpec = current["spec"] as JsonObject;
                if (ModelConfigSpec.RetargetsStoredKey(currentSpec, provider.Type, provider.Schema, values, newKey || command.NoApiKey))
                    return Outcome<ModelConfigDetail>.Conflict(ModelConfigSpec.Read(current, kagent.ModelConfigRef), ModelFailure.KeyAgain(provider.Type), "key");
                var spec = ModelConfigSpec.Edit(currentSpec, provider.Type, provider.Schema, command.Model, values, dropKey: newKey || command.NoApiKey);
                if (provider.Schema.TakesKey && !newKey && !command.NoApiKey && !ModelConfigSpec.HasKey(spec))
                    return Outcome<ModelConfigDetail>.Conflict(ModelConfigSpec.Read(current, kagent.ModelConfigRef), ModelFailure.NoKey(provider.Type), "key");
                details = Details(command, provider, values, spec, baseUrlChanged:
                    !ModelConfigSpec.Endpoints(currentSpec, provider.Schema).SequenceEqual(ModelConfigSpec.Endpoints(spec, provider.Schema)));
                await ModelAudit.WriteAsync(mediator, ModelAudit.UpdateRequested, command.ActorId, command.Namespace, command.Name, details);
                if (ModelConfigSpec.LeavesAStaleKey(currentSpec, provider.Type, newKey))
                    await kagent.UpdateModelConfigAsync(command.Namespace, command.Name, spec, apiKey: null, CancellationToken.None);
                saved = await kagent.UpdateModelConfigAsync(command.Namespace, command.Name, spec, command.ApiKey, CancellationToken.None);
            }
            await ModelAudit.OutcomeAsync(mediator, logger, command.Create ? ModelAudit.Created : ModelAudit.Updated, command.ActorId, command.Namespace, command.Name, details);
            logger.LogWarning("Model {ModelConfig} {Action} by {ActorUserId}: provider {Provider}, model {Model}, new API key {NewApiKey}, no API key {NoApiKey}",
                configRef, command.Create ? "created" : "saved", command.ActorId, provider.Type, command.Model, newKey, command.NoApiKey);
            var detail = ModelConfigSpec.Read(saved, kagent.ModelConfigRef);
            return command.Create ? Outcome<ModelConfigDetail>.Created(detail) : Outcome<ModelConfigDetail>.Ok(detail);
        }
        catch (HttpRequestException ex) when (command.Create && ex.StatusCode == HttpStatusCode.Conflict)
        {
            await FailedAsync(command, details, "exists");
            return Outcome<ModelConfigDetail>.Conflict(null, ModelFailure.Exists(configRef), "exists");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Model {ModelConfig} not saved for {ActorUserId}", configRef, command.ActorId);
            await FailedAsync(command, details, command.Create ? "create" : "update");
            return Outcome<ModelConfigDetail>.Unavailable(ModelFailure.Unavailable);
        }
    }

    // A failure before the intent row changed nothing and leaves no row.
    private Task FailedAsync(SaveModelConfigCommand command, object? details, string reason) => details is null
        ? Task.CompletedTask
        : ModelAudit.OutcomeAsync(mediator, logger, ModelAudit.Failed, command.ActorId, command.Namespace, command.Name, new { details, reason });

    private static object Details(SaveModelConfigCommand command, ProviderChoice provider, IReadOnlyDictionary<string, JsonNode> values, JsonObject spec, bool baseUrlChanged) => new
    {
        @namespace = command.Namespace,
        name = command.Name,
        provider = provider.Type,
        model = command.Model,
        fields = values.Keys.Order(StringComparer.Ordinal).ToList(),
        endpoints = ModelConfigSpec.EndpointOrigins(spec, provider.Schema),
        baseUrlChanged,
        keyChanged = !string.IsNullOrEmpty(command.ApiKey),
        keyRemoved = command.NoApiKey
    };
}
