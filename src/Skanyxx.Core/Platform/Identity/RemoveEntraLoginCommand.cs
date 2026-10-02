using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// The owner removes a person's Microsoft login (D10): the account becomes local-only (its roles and teams stay, now
/// edited by hand), every session ends. An account made by Microsoft sign-in has no password, so it cannot sign in
/// again until invited anew or linked.
/// </summary>
public sealed record RemoveEntraLoginCommand(string ActorId, string UserId) : IRequest<Outcome<PersonDto>>;
