using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Passwords;

internal sealed class RequestPasswordResetValidator : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetValidator() => RuleFor(c => c.Email).NotEmpty().MaximumLength(256).NoControlCharacters();
}
