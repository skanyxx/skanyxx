using MediatR;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>The YAML a form would commit, and what stops it (D361). Touches neither git nor kagent's agents.</summary>
internal sealed class ManifestPreviewHandler(DraftProblems problems, IOptions<StudioOptions> options)
    : IRequestHandler<ManifestPreviewQuery, Outcome<ManifestPreview>>
{
    public async Task<Outcome<ManifestPreview>> Handle(ManifestPreviewQuery query, CancellationToken ct)
    {
        if (!query.User.CanEnter)
            return Outcome<ManifestPreview>.Forbidden(StudioFailure.NotStudio);
        var draft = query.Draft.Normalized();
        var (agentYaml, grantsYaml) = AgentManifest.Render(draft, options.Value.Namespace);
        return Outcome<ManifestPreview>.Ok(new ManifestPreview(agentYaml, grantsYaml, await problems.CheckAsync(draft, ct)));
    }
}
