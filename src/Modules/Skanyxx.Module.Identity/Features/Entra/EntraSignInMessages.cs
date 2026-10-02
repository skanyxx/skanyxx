namespace Skanyxx.Module.Identity.Features.Entra;

/// <summary>What a person is told when a Microsoft sign-in does not end in a session. Generic on purpose: no group names, no reasons beyond these.</summary>
internal static class EntraSignInMessages
{
    public const string Incomplete = "Microsoft sign-in did not complete. Try again.";

    public const string NoAccess = "Your Microsoft account has no access to Skanyxx. Ask the owner to add you to a mapped group.";

    public const string NoAccountType =
        "Skanyxx cannot tell whether your Microsoft account is a member of the organisation or a guest. The owner must add the " +
        "optional claim 'acct' to the ID token (app registration > Token configuration).";

    public const string GraphUnavailable = "Skanyxx could not read your groups from Microsoft Graph. Try again in a moment.";
}
