using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>Whether Microsoft sign-in is on, and (when <paramref name="UserId"/> is given) whether that account is linked.</summary>
public sealed record EntraStatusQuery(string? UserId = null) : IRequest<Outcome<EntraStatus>>;
