using FluentValidation;
using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Module.Identity.Features.Preferences;

internal sealed class SaveOrganizationPreferencesValidator : AbstractValidator<SaveOrganizationPreferencesCommand>
{
    public SaveOrganizationPreferencesValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty().MaximumLength(450);
        RuleFor(c => c.Preferences).NotNull().Custom((set, context) =>
        {
            foreach (var problem in PreferenceCatalog.Problems(set))
                context.AddFailure("Preferences", problem);
        });
    }
}
