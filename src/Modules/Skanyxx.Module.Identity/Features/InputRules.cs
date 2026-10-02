using System.Globalization;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Features;

internal static class InputRules
{
    /// <summary>A NUL would otherwise reach Postgres and fail as a 500; no email or password legitimately holds one.</summary>
    public static IRuleBuilderOptions<T, string> NoControlCharacters<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(value => value is null || !value.Any(char.IsControl)).WithMessage("'{PropertyName}' must not contain control characters.");

    /// <summary>
    /// Shown in the header and on the People page: no control characters, and no format characters either (bidi
    /// overrides, zero-width joiners), which would let a name read as someone else's.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> DisplayName<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(100)
            .Must(n => n is null || !n.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format))
            .WithMessage("Display name must not contain control or invisible formatting characters.");

    /// <summary>
    /// The characters Identity allows in a user name, which is the email: an invite for an address it would refuse
    /// could never be accepted, so the owner hears it at invite time instead of the invitee at accept time.
    /// </summary>
    public static IRuleBuilderOptions<T, string> UserNameCharacters<T>(this IRuleBuilder<T, string> rule, IOptions<IdentityOptions> identity) =>
        rule.Must(email => email is null || email.All(identity.Value.User.AllowedUserNameCharacters.Contains))
            .WithMessage($"'{{PropertyName}}' may contain only ASCII letters, digits and {string.Concat(identity.Value.User.AllowedUserNameCharacters.Where(c => !char.IsAsciiLetterOrDigit(c)))}.");

    /// <summary>
    /// A team or department slug: the id memory scopes key on (<c>team:&lt;slug&gt;</c>), so it has the scope id's shape
    /// (<see cref="Identifier"/>, the memory CHECK constraint too).
    /// </summary>
    public static IRuleBuilderOptions<T, string> OrgSlug<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(Identifier.IsValid)
            .WithMessage("'{PropertyName}' must be 1–128 characters of a-z, 0-9, '.', '_', '@' or '-', starting with a letter or digit.");

    /// <summary>A team or department name, shown to people: required, and held to the display-name rules.</summary>
    public static IRuleBuilderOptions<T, string?> OrgName<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty().DisplayName();

    /// <summary>Only <see cref="SkanyxxRoles.Grantable"/>: the owner role is never given by invite or role change.</summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<string>> GrantableRoles<T>(this IRuleBuilder<T, IReadOnlyList<string>> rule) =>
        rule.Must(roles => roles.All(SkanyxxRoles.Grantable.Contains))
            .WithMessage($"Roles must be among: {string.Join(", ", SkanyxxRoles.Grantable)}. The owner role cannot be granted.");
}
