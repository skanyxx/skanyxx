using FluentValidation;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class StartPreviewValidator : AbstractValidator<StartPreviewCommand>
{
    public StartPreviewValidator()
    {
        RuleFor(r => r.User).NotNull().SetValidator(new StudioUserValidator());
        RuleFor(r => r.Number).InclusiveBetween(1, 999_999);
    }
}
