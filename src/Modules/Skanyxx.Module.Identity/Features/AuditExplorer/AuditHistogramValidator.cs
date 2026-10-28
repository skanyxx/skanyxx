using FluentValidation;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

internal sealed class AuditHistogramValidator : AbstractValidator<AuditHistogramQuery>
{
    public AuditHistogramValidator() => RuleFor(q => q.Filter).SetValidator(new AuditFilterValidator());
}
