namespace Skanyxx.Core.Platform.Identity;

public static class SkanyxxClaims
{
    /// <summary>Stored as a user claim, so it rides in the cookie and bearer principal without a lookup.</summary>
    public const string DisplayName = "display_name";
}
