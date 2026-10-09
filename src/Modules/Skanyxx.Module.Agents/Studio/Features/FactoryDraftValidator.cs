using FluentValidation;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class FactoryDraftValidator : AbstractValidator<FactoryDraftCommand>
{
    public FactoryDraftValidator()
    {
        RuleFor(c => c.User).NotNull().SetValidator(new StudioUserValidator());
        RuleFor(c => c.Request).NotEmpty().MaximumLength(4_000);
    }
}
