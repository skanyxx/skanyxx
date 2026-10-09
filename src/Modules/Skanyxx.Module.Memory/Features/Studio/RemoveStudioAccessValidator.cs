using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class RemoveStudioAccessValidator : AbstractValidator<RemoveStudioAccessCommand>
{
    public RemoveStudioAccessValidator()
    {
        RuleFor(c => c.Actor).NotEmpty().MaximumLength(128);
        RuleFor(c => c.AgentId).Must(Scope.IsValidId).WithMessage("Invalid agent id.");
    }
}
