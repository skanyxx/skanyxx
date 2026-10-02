using FluentValidation;

namespace Skanyxx.Module.Memory.Features;

internal static class TextRules
{
    /// <summary>Postgres text cannot hold U+0000; reject it here instead of failing with a 500 in the database.</summary>
    public static IRuleBuilderOptions<T, string?> NoNulCharacters<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => v is null || !v.Contains('\0')).WithMessage("Must not contain NUL characters.");
}
