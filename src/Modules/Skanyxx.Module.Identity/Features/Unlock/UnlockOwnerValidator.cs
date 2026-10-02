using FluentValidation;

namespace Skanyxx.Module.Identity.Features.Unlock;

internal sealed class UnlockOwnerValidator : AbstractValidator<UnlockOwnerCommand>
{
    public UnlockOwnerValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(256).NoControlCharacters();
        RuleFor(c => c.BootstrapToken).MaximumLength(512);
    }
}
