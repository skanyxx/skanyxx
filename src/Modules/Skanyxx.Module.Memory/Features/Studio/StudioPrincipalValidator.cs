using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class StudioPrincipalValidator : AbstractValidator<StudioPrincipalQuery>
{
    public StudioPrincipalValidator() => RuleFor(q => q.AgentId).Must(Scope.IsValidId).WithMessage("Invalid agent id.");
}
