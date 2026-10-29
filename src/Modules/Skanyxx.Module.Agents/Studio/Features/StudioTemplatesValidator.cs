using FluentValidation;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class StudioTemplatesValidator : AbstractValidator<StudioTemplatesQuery>
{
    public StudioTemplatesValidator() => RuleFor(q => q.User).NotNull().SetValidator(new StudioUserValidator());
}
