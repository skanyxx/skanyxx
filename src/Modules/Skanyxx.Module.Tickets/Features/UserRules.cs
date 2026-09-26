using FluentValidation;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Tickets.Features;

internal static class UserRules
{
    /// <summary>Postgres text cannot hold U+0000: refuse it here rather than fail with a 500 in the database.</summary>
    public static IRuleBuilderOptions<T, string?> NoNul<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => v is null || !v.Contains('\0')).WithMessage("Must not contain NUL characters.");

    /// <summary>Every tickets request carries the caller's <c>X-User-Id</c>. TODO(identity-slice): claims.</summary>
    public static IRuleBuilderOptions<T, string?> ValidUser<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(Identifier.IsValid).OverridePropertyName("identity").WithMessage("A valid X-User-Id header is required.");
}
