using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.SandboxTokens;

internal sealed class IssueSandboxMemoryTokenValidator : AbstractValidator<IssueSandboxMemoryTokenCommand>
{
    public IssueSandboxMemoryTokenValidator()
    {
        RuleFor(c => c.SandboxName).NotEmpty().MaximumLength(128);
        // The sandboxes module's synthetic id (ax-<name>-<nonce>): a sandbox token never names a studio or kagent agent.
        RuleFor(c => c.AgentId).Must(id => Scope.IsValidId(id) && id.StartsWith("ax-", StringComparison.Ordinal))
            .WithMessage("A sandbox memory agent id is ax-<name>-<nonce>.");
        RuleFor(c => c.OwnerId).Must(Scope.IsValidId).WithMessage("Invalid owner id.");
    }
}
