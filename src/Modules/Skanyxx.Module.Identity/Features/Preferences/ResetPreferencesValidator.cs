using FluentValidation;
using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Module.Identity.Features.Preferences;

internal sealed class ResetPreferencesValidator : AbstractValidator<ResetPreferencesCommand>
{
    public ResetPreferencesValidator() => RuleFor(c => c.UserId).NotEmpty().MaximumLength(450);
}
