using FluentValidation;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class StudioChangesValidator : AbstractValidator<StudioChangesQuery>
{
    public StudioChangesValidator() => RuleFor(q => q.User).NotNull().SetValidator(new StudioUserValidator());
}
