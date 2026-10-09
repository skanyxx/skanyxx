using FluentValidation;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class StudioOverviewValidator : AbstractValidator<StudioOverviewQuery>
{
    public StudioOverviewValidator() => RuleFor(q => q.User).NotNull().SetValidator(new StudioUserValidator());
}
