using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>The template gallery (D360): everyone who may enter the studio.</summary>
public sealed record StudioTemplatesQuery(StudioUser User) : IRequest<Outcome<IReadOnlyList<StudioTemplate>>>;
