using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.SignIn;

internal sealed class SignInValidator : AbstractValidator<SignInCommand>
{
    public SignInValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(256).NoControlCharacters();
        RuleFor(c => c.Password).NotEmpty().MaximumLength(128).NoControlCharacters();
        RuleFor(c => c.BootstrapToken).MaximumLength(512);
    }
}
