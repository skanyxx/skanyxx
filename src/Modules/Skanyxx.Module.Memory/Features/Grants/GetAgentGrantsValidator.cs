using FluentValidation;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Grants;

internal sealed class GetAgentGrantsValidator : AbstractValidator<GetAgentGrantsQuery>
{
    public GetAgentGrantsValidator()
    {
        RuleFor(q => q.Caller).SetValidator(new CallerValidator());
        RuleFor(q => q.AgentId).Must(Scope.IsValidId).WithMessage("Invalid agent id.");
    }
}
