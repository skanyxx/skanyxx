namespace Skanyxx.Module.ToolServers.Setup;

/// <summary>
/// Every "how to fix" text of the Tool servers page, in one place. Facts: scripts/dev/first-hour.sh (local run, the
/// supervised port-forward on :8083), deploy/helm/skanyxx/values.yaml (skanyxx.kagent.url, networkPolicy.*) and kagent
/// 0.10.2 (a RemoteMCPServer is rediscovered every 60 s; its reason is on status.conditions and in the controller log).
/// The shape matches the shared setup guide (Skanyxx.Host/Setup): a step is text plus an optional command.
/// </summary>
public static class ToolServerFixes
{
    private const string Pf = "kubectl -n kagent port-forward svc/kagent-controller 8083:8083";
    private const string Upgrade = "helm upgrade skanyxx deploy/helm/skanyxx -n <release-namespace> --reuse-values -f <your-values.yaml>";
    private const string ControllerLog = "kubectl -n kagent logs deploy/kagent-controller --tail=200";
    private const string HelmControllerLog = "kubectl -n <release-namespace> logs deploy/kagent-controller --tail=200";

    public static SetupFix KAgentPortForward(FixFacts facts) => Make(FixCodes.KAgentPortForward, facts,
        title: "Skanyxx cannot reach kagent: the port-forward is not running",
        goal: "The Tool servers page loads kagent's tool servers.",
        cause: $"Skanyxx calls kagent's controller at {facts.KAgentAddress}, an address on this machine, and nothing answered. "
            + "Locally that address is a kubectl port-forward to the kagent-controller Service: it is not running, it points at another cluster, or kagent itself is down.",
        local:
        [
            new("See what is running (Postgres, kagent, the port-forward, the Host).", "scripts/dev/first-hour.sh status"),
            new("Start what is missing: Postgres, kagent and a supervised port-forward on 8083. Safe to re-run.", "scripts/dev/first-hour.sh up"),
            new("Or forward by hand, in a terminal you keep open.", Pf),
            new("Check kagent's pods are Running and Ready.", "kubectl -n kagent get pods"),
            new("If kagent is on another port or host, point Skanyxx at it and restart the Host.", "KAgent__BaseUrl=localhost KAgent__Port=8083 scripts/dev/first-hour.sh host"),
        ],
        helm:
        [
            new("A Helm install never uses a port-forward: Skanyxx calls the bundled kagent's Service, or skanyxx.kagent.url with BYO kagent (kagent.enabled=false)."),
            new("Check the controller is running.", "kubectl -n <release-namespace> get deploy kagent-controller"),
            new("With BYO kagent, set its in-cluster URL and allow Skanyxx's egress to it (networkPolicy.skanyxxEgress.extra, port 8083), then upgrade.", Upgrade),
        ],
        verify:
        [
            new("kagent answers on the forwarded port.", "curl -fsS http://localhost:8083/health"),
            new("Reload /ToolServers: the red line is gone and the servers are listed."),
        ]);

    public static SetupFix KAgentUnreachable(FixFacts facts) => Make(FixCodes.KAgentUnreachable, facts,
        title: "Skanyxx cannot reach kagent's controller",
        goal: "The Tool servers page loads kagent's tool servers.",
        cause: $"Skanyxx calls kagent's controller at {facts.KAgentAddress} and got no answer in time: the connection was refused, the name did not resolve, "
            + "a NetworkPolicy dropped it, or the controller is down.",
        local:
        [
            new("Locally, Skanyxx reaches kagent through a port-forward: point it at localhost:8083 and start the forward.", "scripts/dev/first-hour.sh up"),
            new("Restart the Host with that address.", "KAgent__BaseUrl=localhost KAgent__Port=8083 scripts/dev/first-hour.sh host"),
            new("Check kagent's pods are Running and Ready.", "kubectl -n kagent get pods"),
        ],
        helm:
        [
            new("Check the controller is running and its Service exists.", "kubectl -n <release-namespace> get deploy,svc kagent-controller"),
            new("Read why Skanyxx failed: its log names the kagent call and the error.", "kubectl -n <release-namespace> logs deploy/skanyxx --tail=200"),
            new("With BYO kagent (kagent.enabled=false): skanyxx.kagent.url must be its in-cluster URL (http://kagent-controller.<kagent-namespace>.svc:8083), "
                + "and networkPolicy.skanyxxEgress.extra must allow port 8083 to that namespace. Then upgrade.", Upgrade),
        ],
        verify:
        [
            new("From the Skanyxx pod, kagent answers.", "kubectl -n <release-namespace> exec deploy/skanyxx -- curl -fsS -m 5 http://kagent-controller:8083/health"),
            new("Reload /ToolServers: the red line is gone and the servers are listed."),
        ]);

    public static SetupFix KAgentRefused(FixFacts facts) => Make(FixCodes.KAgentRefused, facts,
        title: "kagent answered with an error",
        goal: "kagent lists its tool servers without an error.",
        cause: $"kagent's controller at {facts.KAgentAddress} answered HTTP {facts.KAgentStatus}. Its message can name cluster internals, so it is in the Skanyxx log, not on this page. "
            + "Common reasons: kagent's database is not ready, kagent is restarting, or it is not the kagent version Skanyxx is built for (0.10.2).",
        local:
        [
            new("Read kagent's side of it.", ControllerLog),
            new("Check the installed kagent version (Skanyxx targets 0.10.2).", "helm -n kagent list"),
            new("Restart the controller if it is stuck.", "kubectl -n kagent rollout restart deploy/kagent-controller"),
        ],
        helm:
        [
            new("Read kagent's side of it.", HelmControllerLog),
            new("Read Skanyxx's side (the kagent call and its status).", "kubectl -n <release-namespace> logs deploy/skanyxx --tail=200"),
            new("Check kagent's Postgres is up: kagent keeps its tool server list there.", "kubectl -n <release-namespace> get pods"),
        ],
        verify:
        [
            new("kagent lists tool servers.", "curl -fsS http://localhost:8083/api/toolservers"),
            new("Reload /ToolServers: the red line is gone."),
        ]);

    public static SetupFix NotRegistered(FixFacts facts) => Make(FixCodes.NotRegistered, facts,
        title: $"kagent does not list {facts.Ref}",
        goal: $"kagent lists {facts.Ref} with its tools.",
        cause: $"Skanyxx created {facts.Ref}, but kagent does not list it. kagent lists a server once its controller has reconciled it: "
            + $"Kubernetes refused the resource, the controller does not watch the namespace {facts.Namespace}, or it has not run yet (allow a minute).",
        local:
        [
            new("See whether the resource exists and what its status says.", $"kubectl -n {facts.Namespace} get remotemcpserver {facts.Name} -o yaml"),
            new("Read the controller's log for this server.", $"{ControllerLog} | grep {facts.Name}"),
            new("If the resource is missing, an owner saves the server again (Edit → Save) on /ToolServers."),
            new($"If the controller does not watch {facts.Namespace}, add the server in a namespace it watches (kagent's controller.watchNamespaces Helm value), e.g. kagent."),
        ],
        helm:
        [
            new("See whether the resource exists and what its status says.", $"kubectl -n {facts.Namespace} get remotemcpserver {facts.Name} -o yaml"),
            new("Read the controller's log for this server.", $"{HelmControllerLog} | grep {facts.Name}"),
            new("If the resource is missing, an owner saves the server again (Edit → Save) on /ToolServers."),
        ],
        verify:
        [
            new("kagent has the resource.", $"kubectl -n {facts.Namespace} get remotemcpserver {facts.Name}"),
            new("Press Refresh on the server: its status turns Ready once tools are listed."),
        ]);

    public static SetupFix DiscoveryFailed(FixFacts facts) => Make(FixCodes.DiscoveryFailed, facts,
        title: $"kagent found no tools on {facts.Ref}",
        goal: $"kagent lists the tools of {facts.Ref}.",
        cause: $"kagent registered {facts.Ref} but discovered no tools. kagent connects to the server every minute; when the connection or the tool listing fails "
            + "it keeps the server with no tools. Skanyxx did not create this server, so it does not know its URL or headers: the reason is on the resource's status and in kagent's log.",
        local:
        [
            new("Read the reason on the resource (Accepted=False carries it).", $"kubectl -n {facts.Namespace} get {Resource(facts)} {facts.Name} -o jsonpath='{{.status.conditions}}'"),
            new("Read the controller's log for this server.", $"{ControllerLog} | grep {facts.Name}"),
            new("Check the URL, protocol and headers in the spec (header values stay in their Secrets; do not print them).", $"kubectl -n {facts.Namespace} get {Resource(facts)} {facts.Name} -o jsonpath='{{.spec}}'"),
        ],
        helm:
        [
            new("Read the reason on the resource.", $"kubectl -n {facts.Namespace} get {Resource(facts)} {facts.Name} -o jsonpath='{{.status.conditions}}'"),
            new("Read the controller's log for this server.", $"{HelmControllerLog} | grep {facts.Name}"),
            new("A server inside the cluster or on a port other than 443 needs NetworkPolicy rules: networkPolicy.controllerEgress.extra (when controllerEgress is on) "
                + "and networkPolicy.modelEgress.extra for the agent pods. Then upgrade.", Upgrade),
        ],
        verify:
        [
            new("Wait a minute (kagent rediscovers every 60 s), then press Refresh on the server: its tools are listed."),
        ]);

    public static SetupFix ServerUnreachable(FixFacts facts) => Make(FixCodes.ServerUnreachable, facts,
        title: $"kagent cannot reach {facts.Ref}",
        goal: $"kagent reaches {facts.Ref} and lists its tools.",
        cause: (facts.LoopbackUrl
                ? $"The URL's host ({facts.UrlOrigin}) is a loopback address. kagent's controller and agent pods run in the cluster, where that address is the pod itself, not your machine. "
                : $"kagent registered {facts.Ref} but found no tools at {facts.UrlOrigin}: it could not connect, or the server did not answer MCP there. ")
            + "kagent retries every minute.",
        local:
        [
            new("A server on your machine: use host.docker.internal (Docker Desktop, kind, OrbStack) instead of localhost in the URL, and make the server listen on an address pods reach. "
                + "An owner changes it with Edit on /ToolServers."),
            new("Check the address from inside the cluster (any HTTP status means it is reachable).",
                $"kubectl -n {facts.Namespace} run mcp-probe --rm -i --restart=Never --image=curlimages/curl -- curl -sS -m 5 -o /dev/null -w '%{{http_code}}\\n' {facts.UrlOrigin}/"),
            new($"Check the path and protocol: streamable HTTP servers usually serve /mcp, SSE servers /sse (this server: {facts.Protocol})."),
            new("Read the controller's reason.", $"{ControllerLog} | grep {facts.Name}"),
        ],
        helm:
        [
            new("A server in the cluster: use its Service DNS name, e.g. http://<service>.<namespace>.svc.cluster.local:<port>/mcp."),
            new("Allow the traffic: networkPolicy.modelEgress.extra for the agent pods, and networkPolicy.controllerEgress.extra when controllerEgress is on "
                + "(kagent's controller discovers the tools). Then upgrade.", Upgrade),
            new("Check the address from inside the cluster.",
                $"kubectl -n {facts.Namespace} run mcp-probe --rm -i --restart=Never --image=curlimages/curl -- curl -sS -m 5 -o /dev/null -w '%{{http_code}}\\n' {facts.UrlOrigin}/"),
            new("Read the controller's reason.", $"{HelmControllerLog} | grep {facts.Name}"),
        ],
        verify:
        [
            new("Wait a minute (kagent rediscovers every 60 s), then press Refresh on the server: its tools are listed and the status is Ready."),
        ]);

    public static SetupFix HeaderSecret(FixFacts facts) => Make(FixCodes.HeaderSecret, facts,
        title: $"{facts.Ref} reads a header from a Secret that may be missing",
        goal: $"Every Secret and key {facts.Ref} reads its headers from exists in {facts.Namespace}, and kagent lists its tools.",
        cause: $"{facts.Ref} found no tools, and it reads {string.Join(", ", facts.SecretHeaders.Select(h => $"{h.Name} from Secret {h.SecretName} (key {h.SecretKey})"))}. "
            + $"Skanyxx did not create those Secrets. When one or its key is missing in {facts.Namespace}, kagent cannot build the headers and discovers nothing; a wrong value makes the server refuse it.",
        local: SecretSteps(facts),
        helm: SecretSteps(facts),
        verify:
        [
            new("Each Secret lists the key (keys only, never the data).",
                string.Join(" && ", facts.SecretHeaders.Select(h => h.SecretName).Distinct().Select(s => KeysOf(facts.Namespace, s)))),
            new("Wait a minute, then press Refresh on the server: its tools are listed."),
        ]);

    public static SetupFix Tls(FixFacts facts) => Make(FixCodes.Tls, facts,
        title: $"{facts.Ref} is https: check its certificate and URL",
        goal: $"kagent connects to {facts.Ref} over TLS and lists its tools.",
        cause: $"{facts.Ref} is at {facts.UrlOrigin} and kagent found no tools. A certificate kagent's pods do not trust (self-signed, a company CA) fails the TLS handshake; "
            + $"so does a host name the certificate does not cover. A wrong path or protocol ({facts.Protocol}) fails after it.",
        local:
        [
            new("See the handshake from inside the cluster.",
                $"kubectl -n {facts.Namespace} run tls-probe --rm -i --restart=Never --image=curlimages/curl -- curl -sv -m 5 -o /dev/null {facts.UrlOrigin}/"),
            new("For a server on your machine, plain http to host.docker.internal is simpler than a self-signed certificate; an owner changes the URL with Edit."),
            new("For a company CA: put the CA in a Secret in the server's namespace and point the resource's spec.tls at it. Skanyxx's form does not set TLS, so an Edit → Save from the page drops it again.",
                $"kubectl -n {facts.Namespace} create secret generic {facts.Name}-ca --from-file=ca.crt=<path-to-ca.pem>"),
            new("Then reference it.", $"kubectl -n {facts.Namespace} patch remotemcpserver {facts.Name} --type merge -p '{{\"spec\":{{\"tls\":{{\"caCertSecretRef\":\"{facts.Name}-ca\",\"caCertSecretKey\":\"ca.crt\"}}}}}}'"),
        ],
        helm:
        [
            new("See the handshake from inside the cluster.",
                $"kubectl -n {facts.Namespace} run tls-probe --rm -i --restart=Never --image=curlimages/curl -- curl -sv -m 5 -o /dev/null {facts.UrlOrigin}/"),
            new("Put the company CA in a Secret and patch spec.tls as above (caCertSecretRef, caCertSecretKey).",
                $"kubectl -n {facts.Namespace} create secret generic {facts.Name}-ca --from-file=ca.crt=<path-to-ca.pem>"),
            new("Read the controller's reason.", $"{HelmControllerLog} | grep {facts.Name}"),
        ],
        verify:
        [
            new("The probe's handshake succeeds (no certificate error)."),
            new("Wait a minute, then press Refresh on the server: its tools are listed."),
        ]);

    public static SetupFix StdioUnsupported(FixFacts facts) => Make(FixCodes.StdioUnsupported, facts,
        title: $"{facts.Ref} is a stdio server: listed here, not managed here",
        goal: $"Decide how {facts.Ref} is managed: with kubectl (as a stdio MCPServer), or over HTTP as a Remote MCP server Skanyxx manages.",
        cause: $"{facts.Ref} is an MCPServer: kagent runs a command in a pod and talks to it over stdio. Skanyxx creates, edits and deletes Remote MCP servers "
            + "(Streamable HTTP or SSE) only, so it lists this one and its tools but does not change it. Agents can still use it.",
        local:
        [
            new("See it and its status.", $"kubectl -n {facts.Namespace} get mcpserver {facts.Name} -o yaml"),
            new("Change or delete it with kubectl (or kmcp).", $"kubectl -n {facts.Namespace} edit mcpserver {facts.Name}"),
            new("To manage it from Skanyxx instead: run it with an HTTP transport (most MCP servers have a --transport http or sse option) behind a Service, "
                + "then an owner adds it on /ToolServers as a Remote MCP server."),
        ],
        helm:
        [
            new("See it and its status.", $"kubectl -n {facts.Namespace} get mcpserver {facts.Name} -o yaml"),
            new("Keep it in the GitOps/kubectl source it came from; Skanyxx will not overwrite it."),
        ],
        verify:
        [
            new("Its tools are listed on /ToolServers (kagent discovered them)."),
        ]);

    private static IReadOnlyList<FixStep> SecretSteps(FixFacts facts) =>
    [
        .. facts.SecretHeaders.Select(h => h.SecretName).Distinct()
            .Select(s => new FixStep($"List the keys of Secret {s} (keys only: never print the data).", KeysOf(facts.Namespace, s))),
        .. facts.SecretHeaders.Select(h => new FixStep(
            $"If Secret {h.SecretName} or its key {h.SecretKey} is missing, create it from a file that holds the {h.Name} value (never type the value on the command line). "
                + "If the Secret already exists, add the key with kubectl edit or recreate it with every key.",
            $"kubectl -n {facts.Namespace} create secret generic {h.SecretName} --from-file={h.SecretKey}=<path-to-file-with-the-value>")),
        new("Or, as the owner, Edit the server on /ToolServers and choose \"Enter the value\" for the header: kagent then keeps it in a Secret of its own."),
    ];

    private static string KeysOf(string? ns, string secret) =>
        $"kubectl -n {ns} get secret {secret} -o go-template='{{{{range $k, $v := .data}}}}{{{{$k}}}}{{{{\"\\n\"}}}}{{{{end}}}}'";

    private static string Resource(FixFacts facts) => facts.Kind == Definition.ToolServerKinds.Stdio ? "mcpserver" : "remotemcpserver";

    private static SetupFix Make(string code, FixFacts facts, string title, string goal, string cause,
        IReadOnlyList<FixStep> local, IReadOnlyList<FixStep> helm, IReadOnlyList<FixStep> verify) =>
        new(code, title, cause, local, helm, verify, AgentInstructions.For(goal, title, cause, facts, local, helm, verify));
}
