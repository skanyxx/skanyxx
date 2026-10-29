using FluentValidation;

namespace Skanyxx.Module.Quotas.Features;

internal sealed class SetPriceValidator : AbstractValidator<SetPriceCommand>
{
    public SetPriceValidator()
    {
        RuleFor(c => c.Model).NotEmpty().MaximumLength(QuotaRules.MaxModel)
            .Matches("^[a-z0-9]([-a-z0-9.]*[a-z0-9])?/[a-z0-9]([-a-z0-9.]*[a-z0-9])?$").WithMessage("A model is its ModelConfig, namespace/name.");
        RuleFor(c => c.PricePer1K).InclusiveBetween(0m, QuotaRules.MaxPricePer1K).When(c => c.PricePer1K is not null);
    }
}
