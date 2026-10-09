using System.Net;
using System.Text.Json.Nodes;
using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Settings.Model;

/// <summary>
/// Creates or updates kagent's ModelConfig over kagent's API (D3). The API key goes inline to kagent, which keeps it in
/// a Secret it owns; Skanyxx never stores it. Who changed what is logged at Warning, like the other security events
/// (never the key). kagent 0.10.2's update has no precondition, so this is read-modify-write and the last writer wins
/// (an owner save racing another owner save or a <c>kubectl</c> edit); a create that loses the race to another create
/// (409) is re-read and saved as an update, once.
/// </summary>
internal sealed class SaveModelSettingsHandler(KAgentApiClient kagent, ILogger<SaveModelSettingsHandler> logger)
    : IRequestHandler<SaveModelSettingsCommand, Outcome<ModelSettings>>
{
    public async Task<Outcome<ModelSettings>> Handle(SaveModelSettingsCommand command, CancellationToken ct)
    {
        var newKey = !string.IsNullOrEmpty(command.ApiKey) && command.Provider != ModelProviders.Ollama;
        var baseUrl = string.IsNullOrEmpty(command.BaseUrl) ? null : command.BaseUrl;
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                var current = await kagent.GetModelConfigAsync(ct);
                var currentSpec = current?["spec"] as JsonObject;
                if (ModelConfigSpec.RetargetsStoredKey(currentSpec, command.Provider, baseUrl, newKey))
                    return Outcome<ModelSettings>.Conflict(ModelConfigSpec.Read(kagent.ModelConfigRef, current),
                        $"Paste the {command.Provider} API key again to send it to a new endpoint.");
                var spec = ModelConfigSpec.Edit(currentSpec, command.Provider, command.Model, baseUrl, newKey);
                if (spec is null)
                    return Outcome<ModelSettings>.Conflict(ModelConfigSpec.Read(kagent.ModelConfigRef, current),
                        $"Paste the {command.Provider} API key: no key is stored for this provider yet.");

                try
                {
                    if (ModelConfigSpec.LeavesAStaleKey(currentSpec, command.Provider, newKey))
                        await kagent.SaveModelConfigAsync(create: false, spec, apiKey: null, ct);
                    var saved = await kagent.SaveModelConfigAsync(current is null, spec, newKey ? command.ApiKey : null, ct);
                    logger.LogWarning("Model settings {ModelConfig} saved by {ActorUserId}: provider {Provider}, model {Model}, new API key {NewApiKey}",
                        kagent.ModelConfigRef, command.ActorId, command.Provider, command.Model, newKey);
                    return Outcome<ModelSettings>.Ok(ModelConfigSpec.Read(kagent.ModelConfigRef, saved));
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict && current is null && attempt == 1)
                {
                    logger.LogWarning("Model settings {ModelConfig} were created meanwhile; saving over them", kagent.ModelConfigRef);
                }
            }
        }
        catch (Exception ex) when (KAgentFailure.Is(ex))
        {
            logger.LogWarning("Model settings {ModelConfig} not saved for {ActorUserId}: {Reason}",
                kagent.ModelConfigRef, command.ActorId, KAgentFailure.Message(ex));
            return Outcome<ModelSettings>.Unavailable(KAgentFailure.Message(ex));
        }
    }
}
