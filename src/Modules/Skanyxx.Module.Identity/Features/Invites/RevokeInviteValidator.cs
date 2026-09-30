using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Invites;

internal sealed class RevokeInviteValidator : AbstractValidator<RevokeInviteCommand>
{
    public RevokeInviteValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.InviteId).NotEmpty().MaximumLength(36).NoControlCharacters();
    }
}
