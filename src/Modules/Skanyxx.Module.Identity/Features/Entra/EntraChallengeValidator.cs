using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Entra;

/// <summary>
/// The completion must be a path on this site (never <c>//host</c> or <c>/\host</c>): it is where the callback redirects.
/// A link needs the current password (D10).
/// </summary>
internal sealed class EntraChallengeValidator : AbstractValidator<EntraChallengeQuery>
{
    public EntraChallengeValidator()
    {
        RuleFor(q => q.CompletionPath).NotEmpty().MaximumLength(2048).NoControlCharacters()
            .Must(p => p.StartsWith('/') && !p.StartsWith("//") && !p.StartsWith("/\\"))
            .WithMessage("'Completion Path' must be a local path.");
        RuleFor(q => q.LinkUserId).MaximumLength(450);
        RuleFor(q => q.LinkPassword).NotEmpty().When(q => q.LinkUserId is not null)
            .WithMessage("Enter your current password to link a Microsoft account.").MaximumLength(1024);
    }
}
