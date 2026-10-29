namespace Skanyxx.Host.Setup.Guides;

/// <summary>Monthly token quotas and the cost estimate. Facts: README "Token quotas".</summary>
public static class QuotasGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "quotas",
        Title: "Token quotas",
        Readme: "Token quotas",
        What: "Limits how many model tokens each person may use in a month. The owner sets limits per person, team and department on /Quotas; " +
              "the tightest one binds. Chat counts prompt and completion tokens from every turn (Chat's own estimate when kagent reports none) " +
              "and refuses a turn once the limit is reached, with a \"Request more\" action. Supervisors manage their own teams within the team quota.",
        Prerequisites:
        [
            "Postgres for memory (the quotas share its database, in their own tables and migration history).",
            "Chat working (see the Chat guide): only chat turns are counted.",
            "Teams and departments on /Org, if limits should apply per team or department.",
        ],
        Settings:
        [
            new("ConnectionStrings:Memory", "Quotas cannot be stored, so the module fails at startup.",
                "The memory database's connection string (local: docker compose Postgres on localhost:55432; Helm: the chart's Postgres secret)."),
            new("Modules:Enabled:quotas", "Unset means on. Set to false to turn quotas off: Chat then has no limits.",
                "true or false.", Optional: true),
        ],
        Local:
        [
            new("Start Postgres and run the Host; the quotas tables are created on start.", "scripts/dev/first-hour.sh host"),
            new("Sign in as the owner, open /Quotas and set a team, department or personal limit (tokens per month)."),
            new("Optionally set a price per 1,000 tokens for each model, for the cost estimate. It is an estimate only, never a bill."),
        ],
        Helm:
        [
            new("Nothing to install: the module ships in the image and uses the memory database the chart already provides."),
            new("Port-forward the UI (or use the ingress), sign in as the owner and set limits on /Quotas.",
                "kubectl -n <namespace> port-forward svc/skanyxx 8080:8080"),
        ],
        Verify:
        [
            new("Chat shows your usage bar; your account page shows the same percentage and your requests."),
            new("Set a small personal limit, chat past it, and see Chat refuse with \"Request more\"; approve the request on /Quotas and chat again."),
            new("The owner's Audit page lists quota.limit.set, quota.requested and quota.request.approved rows with numbers only."),
        ],
        Errors:
        [
            new("Usage marked \"estimated\"", "kagent did not report token usage for those turns; Chat counted characters / 4 instead. Check the model provider reports usage."),
            new("A supervisor gets 403 setting a limit", "Supervisors set only their own teams' members, never themselves, and never above the team or department limit."),
            new("Chat answers 429 with a quota message", "The person reached their limit: they press \"Request more\", or the owner raises the limit."),
        ]);
}
