using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;

namespace Skanyxx.Module.Sandboxes;

public sealed class SandboxesOptions
{
    public const string Section = "Sandboxes";

    /// <summary>
    /// Off unless set: a sandbox runs caller-chosen code inside the network every module's header identity trusts.
    /// Off, the module registers nothing and answers 404 on every route.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Must be true before <see cref="AllowedImages"/> may be non-empty: the operator confirms sandbox egress and
    /// ax-server ingress are fenced (see <c>deploy/sandboxes/</c>).
    /// </summary>
    public bool NetworkIsolationConfirmed { get; set; }

    /// <summary>ax-server's gRPC endpoint. AX speaks plain HTTP/2 (h2c) and has no auth, so only Skanyxx may reach it.</summary>
    [Required]
    public string Address { get; set; } = "http://ax-server.ax-system.svc:8080";

    /// <summary>The AX tenancy scope every call is made in; never empty (an empty atespace lists every atespace).</summary>
    [Required]
    [RegularExpression(DnsLabel.Pattern)]
    public string Atespace { get; set; } = "default";

    /// <summary>
    /// Image prefixes a task may run, e.g. <c>ghcr.io/acme/</c>. Empty means no image may run (fail closed). Starts
    /// empty so a configured list replaces it instead of being appended to by the binder.
    /// </summary>
    public string[] AllowedImages { get; set; } = [];

    /// <summary>Only <c>@sha256:</c>-pinned images may run.</summary>
    public bool RequireDigest { get; set; }

    /// <summary>Upper bound for any CPU request or limit (a Kubernetes quantity).</summary>
    public string MaxCpu { get; set; } = "4";

    /// <summary>Upper bound for any memory request or limit (a Kubernetes quantity).</summary>
    public string MaxMemory { get; set; } = "8Gi";

    /// <summary>Applied when a run leaves the value out; a missing limit is never below the request.</summary>
    public string DefaultCpuRequest { get; set; } = "250m";

    public string DefaultMemoryRequest { get; set; } = "256Mi";
    public string DefaultCpuLimit { get; set; } = "1";
    public string DefaultMemoryLimit { get; set; } = "1Gi";

    /// <summary>A user's AX tasks that have not failed, completed or started terminating (suspended ones count); more is a 429.</summary>
    [Range(1, 1_000)]
    public int MaxActiveTasksPerUser { get; set; } = 3;

    /// <summary>Active tasks in the whole atespace, anyone's; more is a 429. The per-user cap alone trusts a spoofable header.</summary>
    [Range(1, 10_000)]
    public int MaxActiveTasks { get; set; } = 50;

    /// <summary>Overall deadline for counting active tasks before a run; past it the run is refused.</summary>
    [Range(1, 600)]
    public int CountBudgetSeconds { get; set; } = 30;

    /// <summary>Users who may stop, suspend or resume anyone's task, adopt tasks made outside Skanyxx, and write workspaces. TODO(identity-slice): roles.</summary>
    public string[] Supervisors { get; set; } = [];

    /// <summary>
    /// Skanyxx's <c>/mcp/memory</c> as reachable from inside a sandbox. Must stay empty: startup refuses any value
    /// until the sandbox listener with signed tokens exists (D076/D077), because a sandbox would pick its own identity.
    /// </summary>
    public string MemoryMcpUrl { get; set; } = "";

    /// <summary>Deadline for one unary AX call.</summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>A watch ends after this long even if AX keeps the stream open (it closes by itself once the task runs or fails).</summary>
    [Range(1, 3_600)]
    public int WatchSeconds { get; set; } = 120;

    /// <summary>An idle watch sends an SSE comment this often, so proxies do not cut it.</summary>
    [Range(1, 300)]
    public int KeepAliveSeconds { get; set; } = 15;

    [Range(1, 100)]
    public int MaxWatchesPerUser { get; set; } = 2;

    [Range(1, 10_000)]
    public int MaxWatches { get; set; } = 50;

    internal static bool IsEnabled(IConfiguration configuration) => configuration.GetValue<bool>($"{Section}:{nameof(Enabled)}");

    internal static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.UserInfo.Length == 0;

    /// <summary>Every quantity parses, and default request ≤ default limit ≤ max, per resource.</summary>
    internal bool HasValidResources() =>
        KubeQuantity.IsOrdered(DefaultCpuRequest, DefaultCpuLimit, MaxCpu)
        && KubeQuantity.IsOrdered(DefaultMemoryRequest, DefaultMemoryLimit, MaxMemory);
}
