namespace Skanyxx.Host.Setup.Guides;

/// <summary>Microsoft Entra ID sign-in, off by default (D272). Facts: README "Identity and security model" → "Microsoft Entra ID sign-in".</summary>
public static class EntraGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "entra",
        Title: "Microsoft sign-in (Entra ID)",
        Readme: "Identity and security model → Microsoft Entra ID sign-in",
        What: "People sign in with their Microsoft work account and their security groups decide their roles and teams. Off by default: " +
              "the operator makes it available (Identity:EntraAvailable), then the owner configures it on the Microsoft sign-in page. Invites and passwords keep working.",
        Prerequisites:
        [
            "An Entra tenant where you may create an app registration and grant admin consent.",
            "Skanyxx reached over https at a fixed address (plain http only on localhost).",
            "Security groups (ideally role-assignable) to map to roles and teams; never Microsoft 365 groups people can join themselves.",
        ],
        Settings:
        [
            new("Identity:EntraAvailable", "Microsoft sign-in does not exist on this install: no page, no button, no settings endpoint.",
                "true, set by the operator.", MustBeTrue: true),
            new("Identity:PublicBaseUrl", "The redirect URI cannot be built, so sign-in cannot be turned on (409).",
                "Where people reach Skanyxx, e.g. https://skanyxx.example.com (Helm: skanyxx.publicUrl)."),
            new("Identity:DataProtectionCertificatePath", "The client secret is protected by a key ring kept unencrypted in the same database.",
                "A PFX from your PKI (Helm: skanyxx.dataProtection.certificateSecret).", Optional: true),
        ],
        Local:
        [
            new("Run the Host with the switch on (Microsoft needs https outside localhost; locally use http://localhost).",
                "Identity__EntraAvailable=true scripts/dev/first-hour.sh host"),
            new("Entra admin center → App registrations → New: single tenant, platform Web, redirect URI <Identity:PublicBaseUrl>/signin-oidc exactly (the Microsoft sign-in page shows it). Leave implicit grant off."),
            new("Certificates & secrets → a client secret. Paste it on the Microsoft sign-in page only; it is write-only and never shown again. Note its expiry."),
            new("Token configuration → groups claim (Security groups, as Group ID) and optional ID-token claims email and acct (acct is required)."),
            new("Enterprise applications → this app → Assignment required: Yes; assign the mapped groups."),
            new("For people in more than 200 groups: Microsoft Graph application permission GroupMember.Read.All with admin consent (also used by the periodic group re-check)."),
            new("On the Microsoft sign-in page: tenant id, client id, the secret, the group map (group object id → roles and/or teams), tick Offer \"Sign in with Microsoft\", save."),
        ],
        Helm:
        [
            new("Set skanyxx.publicUrl to the https address and skanyxx.extraEnv: [{name: Identity__EntraAvailable, value: \"true\"}]; /signin-oidc and /Entra are already in ingress.prefixPaths."),
            new("Set skanyxx.dataProtection.certificateSecret (keys certificate.pfx, password) so the stored client secret is encrypted at rest."),
            new("Apply, then do the Entra admin center steps and the Microsoft sign-in page as in the local steps.",
                "helm upgrade skanyxx deploy/helm/skanyxx -n <namespace> --reuse-values -f <your-values.yaml>"),
        ],
        Verify:
        [
            new("/Login shows \"Sign in with Microsoft\"; a member of a mapped group signs in and gets the mapped roles (People marks the account managed by Entra)."),
        ],
        Errors:
        [
            new("\"…the owner must add the optional claim 'acct'…\"", "Add the acct optional claim to the ID token in Token configuration."),
            new("502 \"could not read your groups\"", "Grant GroupMember.Read.All (application) with admin consent."),
            new("Sign-in fails on the callback behind a proxy", "Serve https to the browser and pass /signin-oidc through, POST included."),
            new("409 when turning it on", "Identity:PublicBaseUrl is not set."),
        ]);
}
