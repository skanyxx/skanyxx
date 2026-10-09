using FluentValidation;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class StudioFormOptionsValidator : AbstractValidator<StudioFormOptionsQuery>
{
    public StudioFormOptionsValidator() => RuleFor(q => q.User).NotNull().SetValidator(new StudioUserValidator());
}
