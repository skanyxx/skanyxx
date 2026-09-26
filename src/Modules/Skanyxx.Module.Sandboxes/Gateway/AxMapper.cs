using Ax.V1Alpha1;
using Skanyxx.Module.Sandboxes.Domain;
using AxTask = Ax.V1Alpha1.Task;

namespace Skanyxx.Module.Sandboxes.Gateway;

/// <summary>AX wire types ↔ Skanyxx's sandbox types. The wire types never leave the gateway and the handlers.</summary>
internal static class AxMapper
{
    public const string ApiVersion = "ax.io/v1alpha1";
    public const string MemoryServerName = "skanyxx-memory";

    public static SandboxTask ToSandboxTask(AxTask task) => new(
        task.Metadata?.Name ?? "",
        TaskEnv.Read(task, TaskEnv.Owner),
        TaskEnv.Read(task, TaskEnv.AgentId),
        task.Spec?.Image ?? "",
        [.. task.Spec?.Command ?? []],
        [.. task.Spec?.Env.Select(e => e.Name) ?? []],
        [.. task.Spec?.Workspaces.Select(w => new WorkspaceMount(w.Name, NullIfEmpty(w.Path), NullIfEmpty(w.Goal))) ?? []],
        task.Spec?.Resources is { } r ? new SandboxResources(ToQuantity(r.Requests), ToQuantity(r.Limits)) : null,
        task.Spec?.Suspend ?? false,
        task.Status?.Phase ?? "",
        [.. task.Status?.Conditions.Select(c => new SandboxCondition(c.Type, c.Status, c.Reason, c.LastTransitionTime?.ToDateTime())) ?? []],
        task.Metadata?.CreationTimestamp?.ToDateTime());

    public static AxTask ToAxTask(
        string name, string owner, string agentId, string image, IReadOnlyList<string> command, IReadOnlyDictionary<string, string> env,
        IReadOnlyList<WorkspaceMount> workspaces, SandboxResources resources, bool suspend)
    {
        var spec = new TaskSpec { Image = image, Suspend = suspend, Command = { command } };
        spec.Env.AddRange(env.Select(e => new EnvVar { Name = e.Key, Value = e.Value }));
        spec.Env.AddRange(TaskEnv.For(owner, agentId));
        spec.Workspaces.AddRange(workspaces.Select(w => new WorkspaceRef { Name = w.Name, Path = w.Path ?? "", Goal = w.Goal ?? "" }));
        spec.Resources = new ResourceReqs { Requests = ToList(resources.Requests), Limits = ToList(resources.Limits) };
        return new AxTask { ApiVersion = ApiVersion, Kind = "Task", Metadata = new ObjectMeta { Name = name }, Spec = spec };
    }

    public static SandboxWorkspace ToSandboxWorkspace(Workspace workspace)
    {
        var servers = workspace.Spec?.Mcp?.Servers.Select(s => new McpServerEntry(s.Name, s.Endpoint)).ToList() ?? [];
        return new SandboxWorkspace(
            workspace.Metadata?.Name ?? "",
            [.. workspace.Spec?.Git.Select(g => new GitSource(g.Repo, NullIfEmpty(g.Name), NullIfEmpty(g.Branch), NullIfEmpty(g.Dir), g.Depth)) ?? []],
            servers,
            servers.Any(s => s.Name == MemoryServerName));
    }

    public static Workspace ToAxWorkspace(string name, IReadOnlyList<GitSource> git, IReadOnlyList<McpServerEntry> mcpServers, string? memoryMcpUrl)
    {
        var mcp = new MCPConfig();
        mcp.Servers.AddRange(mcpServers.Select(s => new MCPServer { Name = s.Name, Endpoint = s.Endpoint }));
        if (memoryMcpUrl is not null)
            mcp.Servers.Add(new MCPServer { Name = MemoryServerName, Endpoint = memoryMcpUrl });

        var spec = new WorkspaceSpec { Mcp = mcp };
        spec.Git.AddRange(git.Select(g => new GitRepo
        {
            Repo = g.Repo, Name = g.Name ?? "", Branch = g.Branch ?? "", Dir = g.Dir ?? "", Depth = g.Depth
        }));
        return new Workspace { ApiVersion = ApiVersion, Kind = "Workspace", Metadata = new ObjectMeta { Name = name }, Spec = spec };
    }

    public static SandboxModel ToSandboxModel(Model model) =>
        new(model.Metadata?.Name ?? "", model.Spec?.Provider ?? "", model.Spec?.Model ?? "");

    private static ResourceQuantity? ToQuantity(ResourceList? list) =>
        list is null ? null : new ResourceQuantity(NullIfEmpty(list.Cpu), NullIfEmpty(list.Memory));

    private static ResourceList? ToList(ResourceQuantity? quantity) =>
        quantity is null ? null : new ResourceList { Cpu = quantity.Cpu ?? "", Memory = quantity.Memory ?? "" };

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
