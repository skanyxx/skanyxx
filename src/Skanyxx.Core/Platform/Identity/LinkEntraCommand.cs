using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Links the Microsoft account that just signed in to <paramref name="UserId"/>'s own account (they started it, signed
/// in: the result must carry their id). The account becomes Entra-managed: the mapping applies now and at each Microsoft sign-in.
/// </summary>
public sealed record LinkEntraCommand(string UserId) : IRequest<Outcome<SignedIn>>;
