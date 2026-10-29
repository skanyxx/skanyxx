using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>Open proposals and the latest merged or closed ones, each with its timeline (D365). Builders and supervisors.</summary>
public sealed record StudioChangesQuery(StudioUser User) : IRequest<Outcome<IReadOnlyList<StudioChange>>>;
