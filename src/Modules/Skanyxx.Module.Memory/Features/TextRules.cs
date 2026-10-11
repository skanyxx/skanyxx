using FluentValidation;

namespace Skanyxx.Module.Memory.Features;

internal static class TextRules
{
    /// <summary>Postgres text cannot hold U+0000; reject it here instead of failing with a 500 in the database.</summary>
    public static IRuleBuilderOptions<T, string?> NoNulCharacters<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => v is null || !v.Contains('\0')).WithMessage("Must not contain NUL characters.");

    /// <summary>
    /// A lone surrogate has no UTF-8 form: Postgres would fail the write with a 500 and the object store would get U+FFFD
    /// instead of what was sent, so it is refused here for both.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> WellFormedUtf16<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => v is null || IsWellFormed(v)).WithMessage("Must not contain unpaired surrogate characters.");

    private static bool IsWellFormed(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i]))
                continue;
            if (!char.IsHighSurrogate(value[i]) || i + 1 == value.Length || !char.IsLowSurrogate(value[i + 1]))
                return false;
            i++;
        }
        return true;
    }
}
