using FluentValidation;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>Bounded queries: at most <see cref="MaxRange"/> of time, a short search, few chips, allow-listed fields.</summary>
internal sealed class AuditFilterValidator : AbstractValidator<AuditFilter>
{
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(366);
    public const int MaxQuery = 200;
    public const int MaxChips = 20;
    public const int MaxValue = 450;

    private static bool NoControl(string? value) => value is null || !value.Any(char.IsControl);

    public AuditFilterValidator()
    {
        RuleFor(f => f.To).GreaterThan(f => f.From).WithMessage("'to' must be after 'from'.");
        RuleFor(f => f).Must(f => f.To - f.From <= MaxRange).WithName("range").WithMessage($"The time range is at most {MaxRange.TotalDays} days.");
        RuleFor(f => f.Q).MaximumLength(MaxQuery).Must(NoControl).WithMessage("'q' must not contain control characters.");
        RuleFor(f => f.Fields).Must(f => f.Count <= MaxChips).WithMessage($"At most {MaxChips} filters.");
        RuleForEach(f => f.Fields).ChildRules(chip =>
        {
            chip.RuleFor(c => c.Field).Must(AuditFields.Names.Contains).WithMessage($"A filter's field is one of: {string.Join(", ", AuditFields.Names)}.");
            chip.RuleFor(c => c.Value).MaximumLength(MaxValue).Must(NoControl).WithMessage("A filter's value must not contain control characters.");
        });
    }
}
