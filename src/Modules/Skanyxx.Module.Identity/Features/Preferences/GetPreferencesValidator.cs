using FluentValidation;
using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Module.Identity.Features.Preferences;

internal sealed class GetPreferencesValidator : AbstractValidator<GetPreferencesQuery>
{
    public GetPreferencesValidator() => RuleFor(q => q.UserId).NotEmpty().MaximumLength(450);
}
