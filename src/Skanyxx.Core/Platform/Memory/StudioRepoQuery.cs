using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>The recorded agent repo (D119); null before the first one was recorded.</summary>
public sealed record StudioRepoQuery : IRequest<Outcome<StudioRepoIdentity?>>;
