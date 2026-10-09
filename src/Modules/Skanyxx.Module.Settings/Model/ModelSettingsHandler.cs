using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Settings.Model;

internal sealed class ModelSettingsHandler(KAgentApiClient kagent) : IRequestHandler<ModelSettingsQuery, Outcome<ModelSettings>>
{
    public async Task<Outcome<ModelSettings>> Handle(ModelSettingsQuery query, CancellationToken ct)
    {
        try
        {
            return Outcome<ModelSettings>.Ok(ModelConfigSpec.Read(kagent.ModelConfigRef, await kagent.GetModelConfigAsync(ct)));
        }
        catch (Exception ex) when (KAgentFailure.Is(ex))
        {
            return Outcome<ModelSettings>.Unavailable(KAgentFailure.Message(ex));
        }
    }
}
