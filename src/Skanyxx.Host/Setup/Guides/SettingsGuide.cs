namespace Skanyxx.Host.Setup.Guides;

/// <summary>The owner's installation settings (D350). Facts: README "Configuration" → "Installation settings".</summary>
public static class SettingsGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "settings",
        Title: "Installation settings",
        Readme: "Configuration → Installation settings",
        What: "Installation-wide choices the owner makes in the app: the installation's name, the default theme, where each role lands, " +
              "Microsoft sign-in, paid features and how long history is kept. Saved in the identity database, applied without a restart, every change audited.",
        Prerequisites:
        [
            "The identity module and its Postgres (ConnectionStrings:Identity).",
            "For Cloud tools: a licence that includes it (today the deployment flag Skanyxx:Licence:CloudTools=true).",
        ],
        Settings:
        [
            new("ConnectionStrings:Identity", "Nothing can be saved (the Host does not start without it).",
                "A Postgres connection string from your secret store (Helm: the database Secret)."),
            new("Skanyxx:Licence:CloudTools", "Cloud tools stays a locked paid feature.",
                "true, from the licence you bought (Helm: skanyxx.extraEnv Skanyxx__Licence__CloudTools).", Optional: true, MustBeTrue: true),
        ],
        Local:
        [
            new("Run the Host and sign in as the owner.", "scripts/dev/first-hour.sh host"),
            new("Open /Settings, change a value and save; the page says when each one takes effect."),
            new("Optional, a licensed paid feature for a local run.", "Skanyxx__Licence__CloudTools=true scripts/dev/first-hour.sh host"),
        ],
        Helm:
        [
            new("Nothing to deploy: settings live in the identity database and reach every replica within 30 seconds."),
            new("A value in values.yaml (skanyxx.extraEnv) applies until the owner saves one; a saved value wins."),
        ],
        Verify:
        [
            new("Change the installation name on /Settings: the sidebar shows it on the next page."),
            new("/Audit lists a settings.changed row with the key and its old and new values."),
        ],
        Errors:
        [
            new("409 \"Cloud tools is a paid feature…\"", "The licence flag is not set for this install."),
            new("400 with a field's rule", "The value is out of range (for example audit history 1–3650 days)."),
        ]);
}
