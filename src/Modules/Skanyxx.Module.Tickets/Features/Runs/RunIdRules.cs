using FluentValidation;

namespace Skanyxx.Module.Tickets.Features.Runs;

internal static class RunIdRules
{
    public static IRuleBuilderOptions<T, Guid> ValidRunId<T>(this IRuleBuilder<T, Guid> rule) =>
        rule.NotEqual(Guid.Empty).OverridePropertyName("id").WithMessage("Invalid run id.");
}
