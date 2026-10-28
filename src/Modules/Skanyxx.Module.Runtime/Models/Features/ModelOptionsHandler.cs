using System.Text.Json.Nodes;
using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;
using Skanyxx.Module.Runtime.Models.Definition;

namespace Skanyxx.Module.Runtime.Models.Features;

/// <summary>What the form offers, as kagent-ui does: kagent's providers with their fields, and kagent's supported models.</summary>
internal sealed class ModelOptionsHandler(KAgentApiClient kagent, ILogger<ModelOptionsHandler> logger) : IRequestHandler<ModelOptionsQuery, Outcome<ModelFormOptions>>
{
    public async Task<Outcome<ModelFormOptions>> Handle(ModelOptionsQuery query, CancellationToken ct)
    {
        try
        {
            var providers = ProviderChoice.Of(await kagent.GetModelProvidersAsync(ct));
            var models = await kagent.GetSupportedModelsAsync(ct);
            return Outcome<ModelFormOptions>.Ok(new ModelFormOptions(ModelRefs.Split(kagent.ModelConfigRef).Namespace,
            [
                .. providers.Select(p => new ProviderOption(p.Type,
                    [.. p.Schema.Fields.Select(f => new ProviderField(f.Key, f.Kind, p.Required.Contains(f.Key)))],
                    [.. (models[p.Type] as JsonArray ?? []).Select(m => ModelConfigSpec.String(m?["name"])).OfType<string>().Distinct()],
                    p.Schema.TakesKey))
            ]));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Model form options: kagent did not answer");
            return Outcome<ModelFormOptions>.Unavailable(ModelFailure.Unavailable);
        }
    }
}
