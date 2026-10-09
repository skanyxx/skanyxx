using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class SuspendStudioAgentValidator : AbstractValidator<SuspendStudioAgentCommand>
{
    public SuspendStudioAgentValidator()
    {
        RuleFor(c => c.Actor).NotEmpty().MaximumLength(128);
        RuleFor(c => c.AgentId).Must(Scope.IsValidId).WithMessage("Invalid agent id.");
    }
}
