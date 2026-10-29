using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>Renders a form as YAML without touching git (D361): the optional preview of the guided form.</summary>
public sealed record ManifestPreviewQuery(StudioUser User, AgentDraft Draft) : IRequest<Outcome<ManifestPreview>>;
