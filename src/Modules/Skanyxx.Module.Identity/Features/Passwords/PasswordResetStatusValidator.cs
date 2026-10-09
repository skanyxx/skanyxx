using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Passwords;

internal sealed class PasswordResetStatusValidator : AbstractValidator<PasswordResetStatusQuery>
{
    public PasswordResetStatusValidator() => RuleFor(q => q.Token).NotEmpty().MaximumLength(128).NoControlCharacters();
}
