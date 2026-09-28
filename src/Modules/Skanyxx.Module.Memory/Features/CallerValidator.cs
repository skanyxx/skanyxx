using FluentValidation;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features;

/// <summary>Shared child rules; each request still has exactly one top-level validator.</summary>
internal sealed class CallerValidator : AbstractValidator<MemoryCaller>
{
    public CallerValidator()
    {
        RuleFor(c => c)
            .Must(c => c.UserId is not null || c.AgentId is not null)
            .OverridePropertyName("identity")
            .WithMessage("A signed-in user (or, over MCP, an agent secret) is required.");
        RuleFor(c => c.UserId).Must(Scope.IsValidId).When(c => c.UserId is not null).WithName("userId").WithMessage("Invalid user id.");
        RuleFor(c => c.AgentId).Must(Scope.IsValidId).When(c => c.AgentId is not null).WithName("agentId").WithMessage("Invalid agent id.");
    }
}
