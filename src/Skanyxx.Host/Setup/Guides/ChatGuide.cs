namespace Skanyxx.Host.Setup.Guides;

/// <summary>Chat with merged agents through kagent. Facts: README "First hour" (Chat), first-hour.sh `up`.</summary>
public static class ChatGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "chat",
        Title: "Chat",
        Readme: "First hour (local three-plane run) → Chat",
        What: "People chat with merged agents (the seed today). Skanyxx talks to kagent's controller as the signed-in person; Chat is the only place that talks to an agent.",
        Prerequisites:
        [
            "kagent 0.10.2 on a Kubernetes cluster (local: first-hour.sh up installs it; Helm: bundled, or your own with kagent.enabled=false).",
            "The seed agent applied (deploy/kagent/memory/) and a model saved on /Model.",
            "kagent's API kept private: it is unauthenticated (auth.mode unsecure), so only localhost or the cluster may reach it.",
        ],
        Settings:
        [
            new("KAgent:BaseUrl", "Skanyxx does not know where kagent's controller is, so no agent can be listed or asked.",
                "localhost (with KAgent:Port 8083) through the port-forward locally; the chart sets KAgent:IngressUrl to the controller's URL (BYO kagent: skanyxx.kagent.url).",
                Alternative: "KAgent:IngressUrl"),
            new("Identity:PublicBaseUrl", "The Chat page's requests can be refused (403) when opened under an address that is not allowed.",
                "Where people reach Skanyxx, e.g. https://skanyxx.example.com; other addresses go in Skanyxx:AllowedOrigins. Helm: skanyxx.publicUrl."),
        ],
        Local:
        [
            new("Install kagent (sample agents off), apply the seed agent and keep a supervised port-forward on :8083.",
                "KUBE_CONTEXT=<your-context> scripts/dev/first-hour.sh up"),
            new("If you forward by hand instead, bind loopback only.", "kubectl -n kagent port-forward svc/kagent-controller 8083:8083"),
            new("Run the Host; it sets KAgent:BaseUrl=localhost and KAgent:Port=8083.", "scripts/dev/first-hour.sh host"),
            new("Save a model on /Model, then give the seed its memory secret (see the Memory guide)."),
        ],
        Helm:
        [
            new("Bundled kagent: nothing to set; the chart points KAgent:IngressUrl at it. Your own kagent: kagent.enabled=false and skanyxx.kagent.url=http://kagent-controller.<ns>.svc:8083."),
            new("Install the seed agent and its memory secret.", "OWNER_EMAIL=<owner email> OWNER_PASSWORD=<read from your password manager> scripts/helm/seed-agent.sh"),
        ],
        Verify:
        [
            new("kagent answers through the forward.", "curl -fsS http://localhost:8083/health"),
            new("/Chat lists the seed agent and answers a question; the owner's /Debug page shows the kagent check healthy."),
        ],
        Errors:
        [
            new("first-hour.sh up stops: \"localhost:8083 is held by pid …\"", "Another port-forward or process holds :8083; stop it by its pid and run up again."),
            new("Chat answers 502 with a fixed text", "kagent failed the turn: its own text is in the Host's log. Check the model on /Model and kagent's pods."),
            new("Chat answers 403", "The page was opened under an address not in Skanyxx:AllowedOrigins and not the origin of Identity:PublicBaseUrl."),
            new("No agents listed", "Only agents labelled skanyxx.dev/merged=true are listed: apply the seed (first-hour.sh up) or merge one in Studio."),
        ]);
}
