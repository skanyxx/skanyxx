using FluentValidation;

namespace Skanyxx.Module.Identity.Features;

internal static class InputRules
{
    /// <summary>A NUL would otherwise reach Postgres and fail as a 500; no email or password legitimately holds one.</summary>
    public static IRuleBuilderOptions<T, string> NoControlCharacters<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(value => value is null || !value.Any(char.IsControl)).WithMessage("'{PropertyName}' must not contain control characters.");
}
