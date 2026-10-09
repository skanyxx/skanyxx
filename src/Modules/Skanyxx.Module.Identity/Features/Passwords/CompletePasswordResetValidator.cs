using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Passwords;

internal sealed class CompletePasswordResetValidator : AbstractValidator<CompletePasswordResetCommand>
{
    public CompletePasswordResetValidator()
    {
        RuleFor(c => c.Token).NotEmpty().MaximumLength(128).NoControlCharacters();
        // Upper bound keeps one request from buying seconds of hashing.
        RuleFor(c => c.Password).NotEmpty().MaximumLength(128).NoControlCharacters();
    }
}
