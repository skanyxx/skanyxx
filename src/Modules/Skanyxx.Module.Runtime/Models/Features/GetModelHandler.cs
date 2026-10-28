using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;
using Skanyxx.Core.Services;
using Skanyxx.Module.Runtime.Models.Definition;

namespace Skanyxx.Module.Runtime.Models.Features;

internal sealed class GetModelHandler(KAgentApiClient kagent, ILogger<GetModelHandler> logger) : IRequestHandler<GetModelQuery, Outcome<ModelConfigDetail>>
{
    public async Task<Outcome<ModelConfigDetail>> Handle(GetModelQuery query, CancellationToken ct)
    {
        if (!ModelRefs.IsKubeName(query.Namespace) || !ModelRefs.IsKubeName(query.Name))
            return Outcome<ModelConfigDetail>.NotFound(ModelFailure.NotFound);
        try
        {
            return await kagent.GetModelConfigAsync(query.Namespace, query.Name, ct) is { } model
                ? Outcome<ModelConfigDetail>.Ok(ModelConfigSpec.Read(model, kagent.ModelConfigRef))
                : Outcome<ModelConfigDetail>.NotFound(ModelFailure.NotFound);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Model {Namespace}/{Name}: kagent did not answer", query.Namespace, query.Name);
            return Outcome<ModelConfigDetail>.Unavailable(ModelFailure.Unavailable);
        }
    }
}
