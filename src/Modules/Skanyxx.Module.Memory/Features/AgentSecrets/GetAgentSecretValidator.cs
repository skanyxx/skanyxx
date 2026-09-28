using FluentValidation;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.AgentSecrets;

internal sealed class GetAgentSecretValidator : AbstractValidator<GetAgentSecretQuery>
{
    public GetAgentSecretValidator()
    {
        RuleFor(q => q.Caller).SetValidator(new CallerValidator());
        RuleFor(q => q.AgentId).Must(Scope.IsValidId).WithMessage("Invalid agent id.");
    }
}
