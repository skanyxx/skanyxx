using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;
using Skanyxx.Core.Services;
using Skanyxx.Module.Runtime.Models.Definition;

namespace Skanyxx.Module.Runtime.Models.Features;

internal sealed class DefaultModelHandler(KAgentApiClient kagent, ILogger<DefaultModelHandler> logger) : IRequestHandler<DefaultModelQuery, Outcome<ModelConfigDetail?>>
{
    public async Task<Outcome<ModelConfigDetail?>> Handle(DefaultModelQuery query, CancellationToken ct)
    {
        var (ns, name) = ModelRefs.Split(kagent.ModelConfigRef);
        try
        {
            return Outcome<ModelConfigDetail?>.Ok(await kagent.GetModelConfigAsync(ns, name, ct) is { } model
                ? ModelConfigSpec.Read(model, kagent.ModelConfigRef)
                : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Default model {ModelConfig}: kagent did not answer", kagent.ModelConfigRef);
            return Outcome<ModelConfigDetail?>.Unavailable(ModelFailure.Unavailable);
        }
    }
}
