using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Invites;

internal sealed class AcceptInviteValidator : AbstractValidator<AcceptInviteCommand>
{
    public AcceptInviteValidator()
    {
        RuleFor(c => c.Token).NotEmpty().MaximumLength(128).NoControlCharacters();
        // Upper bound keeps one request from buying seconds of hashing.
        RuleFor(c => c.Password).NotEmpty().MaximumLength(128).NoControlCharacters();
        RuleFor(c => c.DisplayName).DisplayName();
    }
}
