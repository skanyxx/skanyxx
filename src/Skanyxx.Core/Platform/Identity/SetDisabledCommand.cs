using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>Disabling ends the person's sessions and refuses their sign-ins until enabled again. Not for the owner.</summary>
public sealed record SetDisabledCommand(string ActorId, string UserId, bool Disabled) : IRequest<Outcome<PersonDto>>;
