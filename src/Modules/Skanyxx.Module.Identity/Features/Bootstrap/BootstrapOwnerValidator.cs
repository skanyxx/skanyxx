using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Bootstrap;

internal sealed class BootstrapOwnerValidator : AbstractValidator<BootstrapOwnerCommand>
{
    public BootstrapOwnerValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(256).EmailAddress().NoControlCharacters();
        // Upper bound keeps one request from buying seconds of hashing.
        RuleFor(c => c.Password).NotEmpty().MaximumLength(128).NoControlCharacters();
        RuleFor(c => c.DisplayName).MaximumLength(100).Must(n => n is null || !n.Any(char.IsControl))
            .WithMessage("Display name must not contain control characters.");
        RuleFor(c => c.BootstrapToken).MaximumLength(512);
    }
}
