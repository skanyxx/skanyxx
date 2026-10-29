using FluentValidation;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features;

internal static class ViewerRules
{
    /// <summary>Every request carries the signed-in user, filled by the endpoint from the principal.</summary>
    public static IRuleBuilderOptions<T, Viewer> SignedIn<T>(this IRuleBuilder<T, Viewer> rule) =>
        rule.Must(v => Identifier.IsValid(v.UserId)).OverridePropertyName("identity").WithMessage("A signed-in user with a valid id is required.");
}
