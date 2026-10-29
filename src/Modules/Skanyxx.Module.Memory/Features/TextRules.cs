using FluentValidation;
using Skanyxx.Core.Platform;

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

    /// <summary>
    /// D391: a card is shared memory (a team, a department, the company), so a credential written into one — by a person
    /// or by an agent that read it somewhere — would reach everyone who can read that scope. Obvious ones are refused.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> NoSecrets<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => v is null || SecretScan.Find(v) is null)
            .WithMessage((_, v) => $"Seems to hold {SecretScan.Find(v!)}: memory never stores credentials. Remove it and keep it in a secret store.");

    /// <summary>
    /// Unicode tag characters (U+E0000–U+E007F) and stray variation selectors are invisible and carry hidden ASCII to a
    /// model that reads the card ("ASCII smuggling"), and they can split a token so the secret scan misses it (D391).
    /// </summary>
    public static IRuleBuilderOptions<T, string?> NoHiddenText<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => v is null || !SecretScan.HasHiddenText(v)).WithMessage("Must not contain Unicode tag characters (U+E0000–U+E007F) or variation selectors that follow no emoji.");

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
