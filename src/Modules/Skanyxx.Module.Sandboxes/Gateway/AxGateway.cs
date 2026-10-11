using System.Runtime.CompilerServices;
using Ax.V1Alpha1;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Sandboxes.Domain;
using AxTask = Ax.V1Alpha1.Task;
using Task = System.Threading.Tasks.Task;

namespace Skanyxx.Module.Sandboxes.Gateway;

/// <summary>The AX client pinned to the configured atespace, with a deadline on every unary call.</summary>
internal sealed class AxGateway(AX.AXClient client, IOptions<SandboxesOptions> options)
{
    /// <summary>A page for the active-sandbox count; counting gives up at <see cref="MaxCounted"/> sandboxes.</summary>
    private const int PageSize = 100;
    public const int MaxCounted = 10_000;

    private string Atespace => options.Value.Atespace;

    private DateTime Deadline => DateTime.UtcNow.AddSeconds(options.Value.TimeoutSeconds);

    /// <summary>Null when AX says NotFound.</summary>
    public async Task<AxTask?> FindSandboxAsync(string name, CancellationToken ct)
    {
        try
        {
            return await client.GetTaskAsync(new GetTaskRequest { Atespace = Atespace, Name = name }, deadline: Deadline, cancellationToken: ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<AxTask>> ListSandboxesAsync(int limit, int offset, CancellationToken ct) =>
        (await client.ListTasksAsync(new ListTasksRequest { Atespace = Atespace, Limit = limit, Offset = offset },
            deadline: Deadline, cancellationToken: ct)).Tasks;

    /// <summary>
    /// Pages through the atespace until an empty page — AX drops unreadable records, so a short page is not the end.
    /// Null when it holds more than <see cref="MaxCounted"/> sandboxes (or AX ignores the offset): a partial count would
    /// undercount, so the caller must refuse. Offset paging over an index AX re-orders on every save is approximate:
    /// a sandbox saved between pages can be missed — AX offers nothing better.
    /// </summary>
    public async Task<ActiveSandboxCount?> CountActiveAsync(string user, CancellationToken ct)
    {
        var (mine, all) = (0, 0);
        for (var offset = 0; ; offset += PageSize)
        {
            var page = await ListSandboxesAsync(PageSize, offset, ct);
            if (page.Count == 0)
                return new ActiveSandboxCount(mine, all);
            if (offset == MaxCounted)
                return null;
            foreach (var sandbox in page.Where(t => SandboxPhases.IsActive(t.Status?.Phase ?? "")))
            {
                all++;
                if (SandboxEnv.Read(sandbox, SandboxEnv.Owner) == user)
                    mine++;
            }
        }
    }

    public async Task<AxTask> UpdateSandboxAsync(AxTask sandbox, CancellationToken ct)
    {
        sandbox.Metadata.Atespace = Atespace;
        return await client.UpdateTaskAsync(new UpdateTaskRequest { Task = sandbox }, deadline: Deadline, cancellationToken: ct);
    }

    public async Task DeleteSandboxAsync(string name, CancellationToken ct) =>
        await client.DeleteTaskAsync(new DeleteTaskRequest { Atespace = Atespace, Name = name }, deadline: Deadline, cancellationToken: ct);

    public async Task<AxTask> SetSuspendedAsync(string name, bool suspend, CancellationToken ct) => suspend
        ? await client.SuspendTaskAsync(new SuspendTaskRequest { Atespace = Atespace, Name = name }, deadline: Deadline, cancellationToken: ct)
        : await client.ResumeTaskAsync(new ResumeTaskRequest { Atespace = Atespace, Name = name }, deadline: Deadline, cancellationToken: ct);

    /// <summary>AX closes the stream once the sandbox is Running, Failed or Completed; the caller's token bounds it otherwise.</summary>
    public async IAsyncEnumerable<WatchTaskResponse> WatchSandboxAsync(string name, [EnumeratorCancellation] CancellationToken ct)
    {
        using var call = client.WatchTask(new WatchTaskRequest { Atespace = Atespace, Name = name }, cancellationToken: ct);
        await foreach (var response in call.ResponseStream.ReadAllAsync(ct))
            yield return response;
    }

    public async Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(CancellationToken ct) =>
        (await client.ListWorkspacesAsync(new ListWorkspacesRequest { Atespace = Atespace }, deadline: Deadline, cancellationToken: ct)).Workspaces;

    public async Task<Workspace> UpdateWorkspaceAsync(Workspace workspace, CancellationToken ct)
    {
        workspace.Metadata.Atespace = Atespace;
        return await client.UpdateWorkspaceAsync(new UpdateWorkspaceRequest { Workspace = workspace }, deadline: Deadline, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<Model>> ListModelsAsync(CancellationToken ct) =>
        (await client.ListModelsAsync(new ListModelsRequest { Atespace = Atespace }, deadline: Deadline, cancellationToken: ct)).Models;
}
