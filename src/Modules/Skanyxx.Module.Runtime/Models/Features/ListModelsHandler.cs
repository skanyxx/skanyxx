using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;
using Skanyxx.Module.Runtime.Models.Definition;

namespace Skanyxx.Module.Runtime.Models.Features;

/// <summary>Every ModelConfig kagent has, by namespace and name, the default marked.</summary>
internal sealed class ListModelsHandler(KAgentApiClient kagent, ILogger<ListModelsHandler> logger) : IRequestHandler<ListModelsQuery, Outcome<ModelList>>
{
    public async Task<Outcome<ModelList>> Handle(ListModelsQuery query, CancellationToken ct)
    {
        try
        {
            var models = (await kagent.GetModelConfigsAsync(ct)).OfType<System.Text.Json.Nodes.JsonObject>()
                .Select(m => ModelConfigSpec.Read(m, kagent.ModelConfigRef))
                .OrderBy(m => m.Namespace, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal);
            return Outcome<ModelList>.Ok(new ModelList(kagent.ModelConfigRef, query.CanWrite, [.. models]));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Models list: kagent did not answer");
            return Outcome<ModelList>.Unavailable(ModelFailure.Unavailable);
        }
    }
}
