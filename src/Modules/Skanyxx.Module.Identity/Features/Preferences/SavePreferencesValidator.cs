using FluentValidation;
using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Module.Identity.Features.Preferences;

/// <summary>Only <see cref="PreferenceCatalog"/>'s values: no free-form text is ever stored or rendered.</summary>
internal sealed class SavePreferencesValidator : AbstractValidator<SavePreferencesCommand>
{
    public SavePreferencesValidator()
    {
        RuleFor(c => c.UserId).NotEmpty().MaximumLength(450);
        RuleFor(c => c.Preferences).NotNull().Custom((set, context) =>
        {
            foreach (var problem in PreferenceCatalog.Problems(set))
                context.AddFailure("Preferences", problem);
        });
    }
}
