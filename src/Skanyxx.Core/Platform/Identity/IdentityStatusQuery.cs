using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>Whether the first owner exists yet. Contract lives in Core so the Host's setup and login pages can ask.</summary>
public sealed record IdentityStatusQuery : IRequest<Outcome<IdentityStatus>>;
