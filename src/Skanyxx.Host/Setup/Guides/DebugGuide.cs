namespace Skanyxx.Host.Setup.Guides;

/// <summary>The owner's Debug page (D353). Facts: README "Configuration" → "Debug page".</summary>
public static class DebugGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "debug",
        Title: "Debug (diagnostics)",
        Readme: "Configuration → Debug page",
        What: "What this Host runs and whether each part it depends on answers: build, uptime, configuration sources (secrets redacted), " +
              "module health, kagent / AX / Postgres / Dragonfly checks, recent warnings and errors, and request timings since start.",
        Prerequisites:
        [
            "Signed in as the owner.",
            "kagent reachable (KAgent:BaseUrl) for the kagent check; AX and Dragonfly only when those features are on.",
        ],
        Settings:
        [
            new("KAgent:BaseUrl", "The kagent check fails: Chat and Agents cannot reach the runtime.",
                "kagent's controller service (Helm sets it); locally http://localhost:8083 through a port-forward.", Alternative: "KAgent:IngressUrl"),
            new("OTEL_EXPORTER_OTLP_ENDPOINT", "Traces and metrics stay in the process; this page still shows recent request timings.",
                "Your OpenTelemetry collector, e.g. http://otel-collector:4317.", Optional: true),
        ],
        Local:
        [
            new("Run the Host and open /Debug as the owner.", "scripts/dev/first-hour.sh host"),
            new("Port-forward kagent if the kagent check fails.", "kubectl -n kagent port-forward svc/kagent-controller 8083:8083"),
        ],
        Helm:
        [
            new("Open https://<your host>/Debug as the owner; /Debug is already in ingress.prefixPaths."),
            new("For logs older than this Host's start, read the pod's logs.", "kubectl -n <namespace> logs deploy/skanyxx --since=1h"),
        ],
        Verify:
        [
            new("Every connectivity row says Healthy or \"not configured\" for a feature that is off."),
        ],
        Errors:
        [
            new("kagent Unhealthy", "KAgent:BaseUrl is wrong or kagent is down; check the port-forward or the kagent pods."),
            new("A postgres check Unhealthy", "The database in that module's connection string is unreachable."),
        ]);
}
