using FluentValidation;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

internal sealed class AuditAnalyticsValidator : AbstractValidator<AuditAnalyticsQuery>
{
    public AuditAnalyticsValidator() => RuleFor(q => q.Filter).SetValidator(new AuditFilterValidator());
}
