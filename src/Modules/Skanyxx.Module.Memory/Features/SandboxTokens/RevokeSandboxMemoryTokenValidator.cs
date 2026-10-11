using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.SandboxTokens;

internal sealed class RevokeSandboxMemoryTokenValidator : AbstractValidator<RevokeSandboxMemoryTokenCommand>
{
    public RevokeSandboxMemoryTokenValidator() =>
        RuleFor(c => c.AgentId).Must(Scope.IsValidId).WithMessage("Invalid agent id.");
}
