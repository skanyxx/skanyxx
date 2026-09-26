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
    /// <summary>A page for the active-task count; counting gives up at <see cref="MaxCounted"/> tasks.</summary>
    private const int PageSize = 100;
    public const int MaxCounted = 10_000;

    private string Atespace => options.Value.Atespace;

    private DateTime Deadline => DateTime.UtcNow.AddSeconds(options.Value.TimeoutSeconds);

    /// <summary>Null when AX says NotFound.</summary>
    public async Task<AxTask?> FindTaskAsync(string name, CancellationToken ct)
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

    public async Task<IReadOnlyList<AxTask>> ListTasksAsync(int limit, int offset, CancellationToken ct) =>
        (await client.ListTasksAsync(new ListTasksRequest { Atespace = Atespace, Limit = limit, Offset = offset },
            deadline: Deadline, cancellationToken: ct)).Tasks;

    /// <summary>
    /// Pages through the atespace until an empty page — AX drops unreadable records, so a short page is not the end.
    /// Null when it holds more than <see cref="MaxCounted"/> tasks (or AX ignores the offset): a partial count would
    /// undercount, so the caller must refuse. Offset paging over an index AX re-orders on every save is approximate:
    /// a task saved between pages can be missed — AX offers nothing better.
    /// </summary>
    public async Task<ActiveTaskCount?> CountActiveAsync(string user, CancellationToken ct)
    {
        var (mine, all) = (0, 0);
        for (var offset = 0; ; offset += PageSize)
        {
            var page = await ListTasksAsync(PageSize, offset, ct);
            if (page.Count == 0)
                return new ActiveTaskCount(mine, all);
            if (offset == MaxCounted)
                return null;
            foreach (var task in page.Where(t => TaskPhases.IsActive(t.Status?.Phase ?? "")))
            {
                all++;
                if (TaskEnv.Read(task, TaskEnv.Owner) == user)
                    mine++;
            }
        }
    }

    public async Task<AxTask> UpdateTaskAsync(AxTask task, CancellationToken ct)
    {
        task.Metadata.Atespace = Atespace;
        return await client.UpdateTaskAsync(new UpdateTaskRequest { Task = task }, deadline: Deadline, cancellationToken: ct);
    }

    public async Task DeleteTaskAsync(string name, CancellationToken ct) =>
        await client.DeleteTaskAsync(new DeleteTaskRequest { Atespace = Atespace, Name = name }, deadline: Deadline, cancellationToken: ct);

    public async Task<AxTask> SetSuspendedAsync(string name, bool suspend, CancellationToken ct) => suspend
        ? await client.SuspendTaskAsync(new SuspendTaskRequest { Atespace = Atespace, Name = name }, deadline: Deadline, cancellationToken: ct)
        : await client.ResumeTaskAsync(new ResumeTaskRequest { Atespace = Atespace, Name = name }, deadline: Deadline, cancellationToken: ct);

    /// <summary>AX closes the stream once the task is Running, Failed or Completed; the caller's token bounds it otherwise.</summary>
    public async IAsyncEnumerable<WatchTaskResponse> WatchTaskAsync(string name, [EnumeratorCancellation] CancellationToken ct)
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
