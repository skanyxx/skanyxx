using FluentValidation;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class SuspendAgentValidator : AbstractValidator<SuspendAgentCommand>
{
    public SuspendAgentValidator()
    {
        RuleFor(c => c.User).NotNull().SetValidator(new StudioUserValidator());
        RuleFor(c => c.Agent).Must(StudioNames.IsValidName).WithMessage("Invalid agent name.");
    }
}
