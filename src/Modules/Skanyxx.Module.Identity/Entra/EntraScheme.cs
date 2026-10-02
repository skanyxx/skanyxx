namespace Skanyxx.Module.Identity.Entra;

internal static class EntraScheme
{
    /// <summary>The OIDC scheme name, and the <c>LoginProvider</c> of every Entra login in <c>identity_user_logins</c>.</summary>
    public const string Name = "entra";

    public const string DisplayName = "Microsoft";

    /// <summary>Stands in for an actor id where the mapping, not a person, made the change (team member <c>AddedBy</c>, audit lines).</summary>
    public const string Actor = "entra-mapping";
}
