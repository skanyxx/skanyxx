namespace Skanyxx.Host.Setup.Guides;

/// <summary>Studio: agents as pull requests in the agent repo. Facts: README "Studio (agents as pull requests)", first-hour.sh `git`/`host`.</summary>
public static class StudioGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "studio",
        Title: "Studio",
        Readme: "Studio (agents as pull requests)",
        What: "Start an agent from a template and a short form (no YAML). Builders save it as a pull request in the agent repo " +
              "skanyxx-agents; supervisors and the owner merge it, or apply their own change directly. CI checks every pull " +
              "request, and each change shows its timeline: pull request, checks, merge, deployment (Argo CD when configured, " +
              "else the reconciler) to kagent. Cloud tools are a paid feature behind a licence switch.",
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
            new("Studio:Ci:Token", "The agent repo's CI cannot ask Skanyxx to check agent folders, so pull requests show no checks.",
                "A long random value you generate (e.g. openssl rand -hex 32); the same value goes into the agent repo's secret SKANYXX_CI_TOKEN. Keep it in a secret store.",
                Optional: true),
            new("Studio:RequireChecks", "Merges do not wait for CI: a supervisor can merge before the checks pass.",
                "true once CI runs on the agent repo.", Optional: true, MustBeTrue: true),
            new("Studio:Argo:BaseUrl", "Deploy status comes from the reconciler and kagent instead of Argo CD.",
                "Argo CD's API server as the Host reaches it, e.g. https://argocd-server.argocd.svc. Needs Studio:Argo:Token (a read-only account token) and Studio:Argo:Application (default skanyxx-agents).",
                Optional: true),
            new("Skanyxx:Licence:CloudTools", "Tool servers listed in Studio:CloudToolServers (they call cloud provider APIs) are shown locked and cannot be added to agents.",
                "true when your licence includes cloud tools; then turn on Settings → Features → Cloud tools. List those servers in both Studio:McpServers and Studio:CloudToolServers.",
                Optional: true, MustBeTrue: true),
        ],
        Local:
        [
            new("Start the bundled git (Gitea on 127.0.0.1:3300) and mint the skanyxx-bot token into the token file.", "scripts/dev/first-hour.sh git"),
            new("Restart the Host: `host` reads the token file and sets Studio:Git:BaseUrl, Studio:Git:Token and Studio:MemoryMcpUrl.", "scripts/dev/first-hour.sh host"),
            new("Open /Studio. The reconciler creates org skanyxx and repo skanyxx-agents on its first pass (it retries while git is down)."),
            new("Optional, CI: generate a CI token and give it to the Host (never paste it into a file in the repo).",
                "read -rs T && export Studio__Ci__Token=\"$T\"; unset T"),
            new("As the owner, open /Studio → \"CI for the agent repo\" → Add CI to the agent repo: one pull request with only the workflow file, merged at once."),
            new("In Gitea, on skanyxx/skanyxx-agents: Settings → Actions → enable; add the variable SKANYXX_URL (http://host.docker.internal:5287) and the secret SKANYXX_CI_TOKEN (the same token); register an act_runner for the org."),
            new("Optional, Argo CD: create an Application for the agent repo (path agents, directory include */agent.yaml, namespace kagent, ignoreDifferences on /metadata/labels) and give the Host Studio__Argo__BaseUrl and Studio__Argo__Token (a read-only account)."),
        ],
        Helm:
        [
            new("Keep the bundled Gitea on (gitea.enabled=true, the default) or point at your own git server."),
            new("Sign in to Gitea as its admin (Secret <release>-gitea), create the account skanyxx-bot (not a site admin) and a token for it with scopes write:organization and write:repository."),
            new("Store the token in a Secret with the key Studio__Git__Token (paste it at the prompt; it never lands in a file or your shell history).",
                "read -rs T && printf %s \"$T\" | kubectl -n <namespace> create secret generic skanyxx-studio-git --from-file=Studio__Git__Token=/dev/stdin; unset T"),
            new("Give it to Skanyxx and set the addresses, in your values file: skanyxx.extraEnvFrom: [{secretRef: {name: skanyxx-studio-git}}] and skanyxx.extraEnv: [{name: Studio__Git__BaseUrl, value: \"http://<release>-gitea.<namespace>.svc:3000\"}, {name: Studio__MemoryMcpUrl, value: \"http://<release>.<namespace>.svc:8081/mcp/memory\"}]. The chart does not name these settings itself."),
            new("Apply it.", "helm upgrade skanyxx deploy/helm/skanyxx -n <namespace> --reuse-values -f <your-values.yaml>"),
            new("Optional, CI: store a CI token in the same Secret (key Studio__Ci__Token) and set Studio__RequireChecks=true in skanyxx.extraEnv once CI runs.",
                "read -rs T && kubectl -n <namespace> create secret generic skanyxx-studio-ci --from-literal=Studio__Ci__Token=\"$T\"; unset T"),
            new("As the owner, Add CI to the agent repo on /Studio; in Gitea enable Actions on the repo, set SKANYXX_URL (http://<release>.<namespace>.svc:8080) and the secret SKANYXX_CI_TOKEN, and register a runner."),
            new("Optional, Argo CD: an Application syncing the agent repo's agents/*/agent.yaml into kagent's namespace (ignoreDifferences on /metadata/labels, which the reconciler sets); a read-only Argo account token in a Secret with key Studio__Argo__Token, and Studio__Argo__BaseUrl in skanyxx.extraEnv."),
            new("Optional, cloud tools: list the cloud tool servers in Studio__McpServers__N and Studio__CloudToolServers__N, and set Skanyxx__Licence__CloudTools=true if your licence includes them; then the owner turns on Settings → Features → Cloud tools."),
        ],
        Verify:
        [
            new("Open /Studio: the page shows the form and no \"needs the agent repo\" message."),
            new("Propose a small agent: a pull request appears in skanyxx-agents; a supervisor merges it and the agent shows up in Chat within Studio:ReconcileSeconds (60)."),
            new("Under \"Changes and deployments\", the change shows Checks passed (with CI) and Deployed when kagent (or Argo CD) reports it healthy."),
        ],
        Errors:
        [
            new("\"The studio needs the agent repo: set Studio:Git:BaseUrl and Studio:Git:Token\"", "Both settings are required; set them and restart the Host."),
            new("The studio stops with a repo problem (gone, public, different, or main unprotected)",
                "The repo's git id is recorded and never re-created: restore it, or the owner confirms on /Studio (POST api/studio/confirm)."),
            new("Checks stay \"no CI\"", "The workflow is not in main, Actions are off on the repo, or no runner picked the job: see Gitea's Actions tab."),
            new("CI says \"Skanyxx answered HTTP 401\"", "The repo secret SKANYXX_CI_TOKEN differs from Studio:Ci:Token; set both again (404 means Studio:Ci:Token is not set)."),
            new("A cloud tool is \"Paid · locked\"", "Skanyxx:Licence:CloudTools is not true, or the owner has not turned on Settings → Features → Cloud tools; agents already merged keep running, new ones (Studio or the Agents page) cannot add it."),
            new("Deploy shows \"Argo CD cannot be reached\"", "Check Studio:Argo:BaseUrl from the Host and that the token may read the Application (applications, get)."),
            new("No token file after first-hour.sh git", "Gitea did not issue a token: check the gitea container (docker compose -p skanyxx logs gitea) and run `git` again."),
        ]);
}
