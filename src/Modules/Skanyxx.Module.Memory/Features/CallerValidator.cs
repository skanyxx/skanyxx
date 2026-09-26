using FluentValidation;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features;

/// <summary>Shared child rules; each request still has exactly one top-level validator.</summary>
internal sealed class CallerValidator : AbstractValidator<Caller>
{
    public CallerValidator()
    {
        RuleFor(c => c)
            .Must(c => c.UserId is not null || c.AgentId is not null)
            .OverridePropertyName("identity")
            .WithMessage("X-User-Id (or, over MCP, X-Agent-Id) header is required.");
        RuleFor(c => c.UserId).Must(Scope.IsValidId).When(c => c.UserId is not null).WithName("X-User-Id").WithMessage("Invalid X-User-Id.");
        RuleFor(c => c.AgentId).Must(Scope.IsValidId).When(c => c.AgentId is not null).WithName("X-Agent-Id").WithMessage("Invalid X-Agent-Id.");
    }
}
