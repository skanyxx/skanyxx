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
/// Secret it owns; Skanyxx never stores, returns or logs it. Who changed what is logged at Warning, like the other
/// security events. kagent 0.10.2's update has no precondition, so an edit is read-modify-write and the last writer wins.
/// </summary>
internal sealed class SaveModelConfigHandler(KAgentApiClient kagent, ILogger<SaveModelConfigHandler> logger)
    : IRequestHandler<SaveModelConfigCommand, Outcome<ModelConfigDetail>>
{
    public async Task<Outcome<ModelConfigDetail>> Handle(SaveModelConfigCommand command, CancellationToken ct)
    {
        var configRef = $"{command.Namespace}/{command.Name}";
        var newKey = !string.IsNullOrEmpty(command.ApiKey);
        try
        {
            var provider = ProviderChoice.Of(await kagent.GetModelProvidersAsync(ct)).FirstOrDefault(p => p.Type == command.Provider)
                ?? throw ModelFailure.Invalid([$"kagent does not offer the provider '{command.Provider}'."]);
            var (values, problems) = ModelParams.Check(provider, command.Params);
            if (problems.Count > 0)
                throw ModelFailure.Invalid(problems);

            JsonObject saved;
            if (command.Create)
            {
                if (provider.Schema.TakesKey && !newKey && !command.NoApiKey)
                    throw ModelFailure.Invalid([ModelFailure.NoKey(provider.Type)]);
                var spec = ModelConfigSpec.Edit(null, provider.Type, provider.Schema, command.Model, values, dropKey: true);
                saved = await kagent.CreateModelConfigAsync(command.Namespace, command.Name, spec, command.ApiKey, ct);
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
                if (ModelConfigSpec.LeavesAStaleKey(currentSpec, provider.Type, newKey))
                    await kagent.UpdateModelConfigAsync(command.Namespace, command.Name, spec, apiKey: null, ct);
                saved = await kagent.UpdateModelConfigAsync(command.Namespace, command.Name, spec, command.ApiKey, ct);
            }
            logger.LogWarning("Model {ModelConfig} {Action} by {ActorUserId}: provider {Provider}, model {Model}, new API key {NewApiKey}, no API key {NoApiKey}",
                configRef, command.Create ? "created" : "saved", command.ActorId, provider.Type, command.Model, newKey, command.NoApiKey);
            var detail = ModelConfigSpec.Read(saved, kagent.ModelConfigRef);
            return command.Create ? Outcome<ModelConfigDetail>.Created(detail) : Outcome<ModelConfigDetail>.Ok(detail);
        }
        catch (HttpRequestException ex) when (command.Create && ex.StatusCode == HttpStatusCode.Conflict)
        {
            return Outcome<ModelConfigDetail>.Conflict(null, ModelFailure.Exists(configRef), "exists");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Model {ModelConfig} not saved for {ActorUserId}", configRef, command.ActorId);
            return Outcome<ModelConfigDetail>.Unavailable(ModelFailure.Unavailable);
        }
    }
}
