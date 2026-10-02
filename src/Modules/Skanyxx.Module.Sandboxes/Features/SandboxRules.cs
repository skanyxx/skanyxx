using FluentValidation;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Sandboxes.Features;

internal static class SandboxRules
{
    /// <summary>Every sandboxes request carries the signed-in user's id, filled by the endpoint from the principal.</summary>
    public static IRuleBuilderOptions<T, string?> ValidUser<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(Identifier.IsValid).OverridePropertyName("identity").WithMessage("A signed-in user with a valid id is required.");

    public static IRuleBuilderOptions<T, string> ValidName<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(DnsLabel.IsValid).OverridePropertyName("name")
            .WithMessage("Must be a DNS label: lowercase letters, digits and '-', at most 63, starting and ending alphanumeric.");

    public static IRuleBuilderOptions<T, string?> NoNul<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => v is null || !v.Contains('\0')).WithMessage("Must not contain NUL characters.");
}
