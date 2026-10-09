using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Passwords;

internal sealed class IssuePasswordResetValidator : AbstractValidator<IssuePasswordResetCommand>
{
    public IssuePasswordResetValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.UserId).NotEmpty().MaximumLength(450).NoControlCharacters();
    }
}
