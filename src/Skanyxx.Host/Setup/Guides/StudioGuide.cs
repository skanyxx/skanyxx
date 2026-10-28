namespace Skanyxx.Host.Setup.Guides;

/// <summary>Studio: agents as pull requests in the agent repo. Facts: README "Studio (agents as pull requests)", first-hour.sh `git`/`host`.</summary>
public static class StudioGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "studio",
        Title: "Studio",
        Readme: "Studio (agents as pull requests)",
        What: "Builders compose agents in a form (or let the factory draft them) and save each one as a pull request in the " +
              "agent repo skanyxx-agents. Nothing is live until a supervisor merges it; the reconciler then applies it to kagent.",
        Prerequisites:
        [
            "A git server Skanyxx can reach: the bundled Gitea (local: first-hour.sh git; Helm: gitea.enabled, the default) or your own Gitea.",
            "A git account for Skanyxx (skanyxx-bot, not a site admin) and a token for it with scopes write:organization and write:repository.",
            "kagent running and reachable (see the Chat guide), and the owner's model saved on /Model.",
        ],
        Settings:
        [
            new("Studio:Git:BaseUrl", "The studio has no git server to keep the agent repo in, so nothing can be proposed or merged.",
                "The git server's address as the Host reaches it: http://localhost:3300 locally, http://<release>-gitea.<namespace>.svc:3000 on the chart."),
            new("Studio:Git:Token", "Skanyxx cannot sign in to git, so it cannot create the repo or open pull requests.",
                "A token of the skanyxx-bot git account (scopes write:organization, write:repository). first-hour.sh git writes one to ~/.config/skanyxx/gitea-token (mode 0600). Keep it in a secret store; never paste it into a file in the repo."),
            new("Studio:MemoryMcpUrl", "Agents with memory grants cannot be merged: they have no address to reach company memory at.",
                "/mcp/memory as kagent's pods reach it: http://host.docker.internal:5287/mcp/memory locally, http://<release>.<namespace>.svc:8081/mcp/memory on the chart.",
                Optional: true),
            new("Studio:McpServers", "Builders can attach no tool servers besides memory.",
                "Names of kagent RemoteMCPServers (in Studio:Namespace) builders may attach.", Optional: true),
        ],
        Local:
        [
            new("Start the bundled git (Gitea on 127.0.0.1:3300) and mint the skanyxx-bot token into the token file.", "scripts/dev/first-hour.sh git"),
            new("Restart the Host: `host` reads the token file and sets Studio:Git:BaseUrl, Studio:Git:Token and Studio:MemoryMcpUrl.", "scripts/dev/first-hour.sh host"),
            new("Open /Studio. The reconciler creates org skanyxx and repo skanyxx-agents on its first pass (it retries while git is down)."),
        ],
        Helm:
        [
            new("Keep the bundled Gitea on (gitea.enabled=true, the default) or point at your own git server."),
            new("Sign in to Gitea as its admin (Secret <release>-gitea), create the account skanyxx-bot (not a site admin) and a token for it with scopes write:organization and write:repository."),
            new("Store the token in a Secret with the key Studio__Git__Token (paste it at the prompt; it never lands in a file or your shell history).",
                "read -rs T && printf %s \"$T\" | kubectl -n <namespace> create secret generic skanyxx-studio-git --from-file=Studio__Git__Token=/dev/stdin; unset T"),
            new("Give it to Skanyxx and set the addresses, in your values file: skanyxx.extraEnvFrom: [{secretRef: {name: skanyxx-studio-git}}] and skanyxx.extraEnv: [{name: Studio__Git__BaseUrl, value: \"http://<release>-gitea.<namespace>.svc:3000\"}, {name: Studio__MemoryMcpUrl, value: \"http://<release>.<namespace>.svc:8081/mcp/memory\"}]. The chart does not name these settings itself."),
            new("Apply it.", "helm upgrade skanyxx deploy/helm/skanyxx -n <namespace> --reuse-values -f <your-values.yaml>"),
        ],
        Verify:
        [
            new("Open /Studio: the page shows the form and no \"needs the agent repo\" message."),
            new("Propose a small agent: a pull request appears in skanyxx-agents; a supervisor merges it and the agent shows up in Chat within Studio:ReconcileSeconds (60)."),
        ],
        Errors:
        [
            new("\"The studio needs the agent repo: set Studio:Git:BaseUrl and Studio:Git:Token\"", "Both settings are required; set them and restart the Host."),
            new("The studio stops with a repo problem (gone, public, different, or main unprotected)",
                "The repo's git id is recorded and never re-created: restore it, or the owner confirms on /Studio (POST api/studio/confirm)."),
            new("No token file after first-hour.sh git", "Gitea did not issue a token: check the gitea container (docker compose -p skanyxx logs gitea) and run `git` again."),
        ]);
}
