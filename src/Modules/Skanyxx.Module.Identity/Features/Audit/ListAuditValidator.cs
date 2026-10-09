using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Audit;

internal sealed class ListAuditValidator : AbstractValidator<ListAuditQuery>
{
    public const int MaxLimit = 200;

    private static bool NoControl(string? value) => value is null || !value.Any(char.IsControl);

    public ListAuditValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, MaxLimit);
        RuleFor(q => q.Before).GreaterThan(0);
        RuleFor(q => q.Action).MaximumLength(64).Must(NoControl).WithMessage("'Action' must not contain control characters.");
        RuleFor(q => q.UserId).MaximumLength(450).Must(NoControl).WithMessage("'User Id' must not contain control characters.");
    }
}
