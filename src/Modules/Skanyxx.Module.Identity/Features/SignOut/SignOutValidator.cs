using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.SignOut;

internal sealed class SignOutValidator : AbstractValidator<SignOutCommand>
{
    public SignOutValidator() => RuleFor(c => c.UserId).NotEmpty();
}
