using FluentValidation;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.AgentSecrets;

internal sealed class IssueAgentSecretValidator : AbstractValidator<IssueAgentSecretCommand>
{
    public IssueAgentSecretValidator()
    {
        RuleFor(c => c.Caller).SetValidator(new CallerValidator());
        RuleFor(c => c.AgentId).Must(Scope.IsValidId).WithMessage("Invalid agent id.");
    }
}
