using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Finishes a Microsoft sign-in from the external cookie the OIDC callback left: maps the groups (D027), then creates
/// the account, re-maps it, or refuses, and on success starts a cookie session like a password sign-in.
/// </summary>
public sealed record CompleteEntraSignInCommand : IRequest<Outcome<SignedIn>>;
