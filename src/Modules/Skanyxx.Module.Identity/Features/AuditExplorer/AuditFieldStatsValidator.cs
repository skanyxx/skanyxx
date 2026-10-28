using FluentValidation;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

internal sealed class AuditFieldStatsValidator : AbstractValidator<AuditFieldStatsQuery>
{
    public AuditFieldStatsValidator() => RuleFor(q => q.Filter).SetValidator(new AuditFilterValidator());
}
