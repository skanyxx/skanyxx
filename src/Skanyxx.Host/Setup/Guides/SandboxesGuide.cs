namespace Skanyxx.Host.Setup.Guides;

/// <summary>Sandboxes on Google AX (Experimental). Facts: README "Sandboxes on Google AX (Experimental)", deploy/sandboxes/README.md.</summary>
public static class SandboxesGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "sandboxes",
        Title: "Sandboxes (Google AX, experimental)",
        Readme: "Sandboxes on Google AX (Experimental)",
        What: "Only supervisors and the owner run sandboxes: the page and the whole api/sandboxes API refuse employees and builders (D392), " +
              "because a sandbox runs a container image on cluster compute. They run containers in Google AX sandboxes (Agent Substrate), " +
              "watch them start, suspend, resume and stop them; a person who loses supervisor has their running sandboxes stopped. " +
              "Off unless enabled (the settings below turn it on); AX is pre-1.0 and has no authentication, so the network fence is part of the setup, not an extra.",
        Prerequisites:
        [
            "Agent Substrate running on the cluster, with a WorkerPool and AX's base ActorTemplate (ax-system/default-template) selecting it; without them every sandbox fails with \"no free workers available\".",
            "AX v0.3.1 (ax-server + ax-controller): your own, or installed by the chart (ax.install).",
            "The NetworkPolicies of deploy/sandboxes/ applied to the WorkerPool's namespace, and the egress test in deploy/sandboxes/README.md passed from inside a sandbox on this cluster.",
            "A task image under a registry prefix you trust.",
        ],
        Settings:
        [
            new("Sandboxes:Enabled", "The sandboxes module is off: it registers nothing and the page cannot run anything.",
                "true to turn it on (Helm: ax.enabled=true).", MustBeTrue: true),
            new("Sandboxes:Address", "Skanyxx does not know where AX's ax-server is.",
                "AX's gRPC address (h2c), e.g. http://ax-server.ax-system.svc:8080; locally http://127.0.0.1:<port> through a port-forward. Helm sets it from ax.address or ax.install."),
            new("Sandboxes:AllowedImages", "No image is allowed, so every run is refused.",
                "Registry/repo prefixes ending in / (e.g. ghcr.io/acme/). Helm: ax.allowedImages."),
            new("Sandboxes:NetworkIsolationConfirmed", "Startup refuses any allowed image until the network fence is confirmed.",
                "true, only after the egress test in deploy/sandboxes/README.md passed. Helm: ax.networkIsolationConfirmed.", MustBeTrue: true),
            new("Sandboxes:MemoryMcpUrl", "Sandboxes cannot use company memory (attachMemory is refused).",
                "The sandbox listener's /mcp/memory (port Memory:SandboxMcpPort, with Memory:SandboxTokenKey set). Helm: ax.sandboxes.memory.enabled.",
                Optional: true),
        ],
        Local:
        [
            new("Bring up Substrate and AX v0.3.1 on a kind cluster (README \"Local AX on kind\" has the recipe), with a WorkerPool and the base ActorTemplate."),
            new("Port-forward ax-server on loopback only (AX has no auth; never bind it wider).",
                "kubectl -n ax-system port-forward --address 127.0.0.1 svc/ax-server 18080:8080"),
            new("Apply the sandbox NetworkPolicies to the WorkerPool's namespace, then run the egress test from deploy/sandboxes/README.md.",
                "kubectl apply -k deploy/sandboxes/"),
            new("Start the Host with the module on.",
                "Sandboxes__Enabled=true Sandboxes__Address=http://127.0.0.1:18080 Sandboxes__AllowedImages__0=<your-image-prefix> Sandboxes__NetworkIsolationConfirmed=true scripts/dev/first-hour.sh host"),
        ],
        Helm:
        [
            new("Your own AX: set ax.enabled=true, ax.address=<ax-server address>, ax.allowedImages and ax.sandboxes.namespace=<the WorkerPool's namespace>."),
            new("Or let the chart install AX: ax.enabled=true, ax.install=true, both AX images pinned by digest (ax.server.image.*, ax.controller.image.*), ax.substrate.endpoint, ax.snapshotsBucket, ax.sandboxes.namespace."),
            new("Apply, then run the egress test from inside a sandbox (deploy/sandboxes/README.md).",
                "helm upgrade skanyxx deploy/helm/skanyxx -n <namespace> --reuse-values -f <your-values.yaml>"),
            new("Only after the test passed: set ax.networkIsolationConfirmed=true and upgrade again. Optional memory: ax.sandboxes.memory.enabled=true."),
        ],
        Verify:
        [
            new("Open /Sandboxes as the owner or a supervisor: the run form is shown (not \"Sandboxes are off\")."),
            new("As an employee or a builder, /Sandboxes is refused and PUT api/sandboxes/<name> answers 403: give the person the supervisor role to let them run sandboxes."),
            new("Run a sandbox with an allowed image: it goes Pending → Running in the live view; stop it afterwards."),
        ],
        Errors:
        [
            new("Startup refuses AllowedImages", "Sandboxes:NetworkIsolationConfirmed must be true, and only after the NetworkPolicies are applied and verified."),
            new("\"AX unreachable.\" (502)", "The port-forward or ax-server is down, or Sandboxes:Address is wrong."),
            new("Sandbox fails with \"no free workers available\"", "Create the WorkerPool and the base ActorTemplate (kubectl-ate create actor-template)."),
            new("A resumed sandbox ends Failed", "Resume is unreliable on AX v0.3.1: stop the sandbox and run it again."),
            new("403 on api/sandboxes", "Only supervisors and the owner may use sandboxes (D392); an employee or a builder is refused by design."),
            new("Saving someone's roles answers 500 \"Its sandboxes could not be stopped\"", "Removing supervisor stops their sandboxes through AX; AX was unreachable. Fix AX and save the same roles again."),
        ]);
}
