using FluentValidation;

namespace Skanyxx.Module.Quotas.Features;

internal sealed class RequestMoreValidator : AbstractValidator<RequestMoreCommand>
{
    public RequestMoreValidator()
    {
        RuleFor(c => c.Amount).InclusiveBetween(1, QuotaRules.MaxTokens);
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(QuotaRules.MaxReason);
    }
}
