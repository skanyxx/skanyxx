using FluentValidation;
using Skanyxx.Module.Quotas.Data;

namespace Skanyxx.Module.Quotas.Features;

internal sealed class SetLimitValidator : AbstractValidator<SetLimitCommand>
{
    public SetLimitValidator()
    {
        RuleFor(c => c.Kind).Must(QuotaScopes.All.Contains).WithMessage("A limit is on a user, a team or a department.");
        RuleFor(c => c.Id).NotEmpty().MaximumLength(QuotaRules.MaxId);
        RuleFor(c => c.MonthlyTokens).InclusiveBetween(0, QuotaRules.MaxTokens).When(c => c.MonthlyTokens is not null);
    }
}
