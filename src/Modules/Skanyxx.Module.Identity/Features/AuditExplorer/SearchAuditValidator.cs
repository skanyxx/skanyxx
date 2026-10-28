using FluentValidation;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

internal sealed class SearchAuditValidator : AbstractValidator<SearchAuditQuery>
{
    public const int MaxLimit = 200;

    public SearchAuditValidator()
    {
        RuleFor(q => q.Filter).SetValidator(new AuditFilterValidator());
        RuleFor(q => q.Limit).InclusiveBetween(1, MaxLimit);
    }
}
