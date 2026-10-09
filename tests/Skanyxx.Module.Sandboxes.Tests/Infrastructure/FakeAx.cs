using System.Collections.Concurrent;
using Ax.V1Alpha1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using AxTask = Ax.V1Alpha1.Task;
using TaskStatus = Ax.V1Alpha1.TaskStatus;
using Task = System.Threading.Tasks.Task;

namespace Skanyxx.Module.Sandboxes.Tests.Infrastructure;

/// <summary>
/// An in-memory ax-server implementing the generated service base, with AX v0.3.1's observable behaviour: upsert
/// saves as Pending, delete marks Terminating, suspend/resume flip the flag, watch sends INITIAL then MODIFIED and
/// closes once Running. Failures and watch shapes are scripted; every request's atespace is recorded.
/// </summary>
public sealed class FakeAx : AX.AXBase
{
    /// <summary>Upstream error text a response must never echo.</summary>
    public const string SecretDetail = "dial tcp 10.1.2.3:8080 redis://ax:hunter2@redis";

    private readonly ConcurrentDictionary<string, AxTask> _tasks = new();
    private readonly ConcurrentDictionary<string, Workspace> _workspaces = new();
    private readonly ConcurrentDictionary<string, Model> _models = new();
    private readonly ConcurrentQueue<string> _atespaces = new();

    public IReadOnlyList<string> Atespaces => [.. _atespaces];
    public List<UpdateTaskRequest> Updates { get; } = [];
    public List<UpdateWorkspaceRequest> WorkspaceUpdates { get; } = [];
    public int DeleteCalls;

    /// <summary>Every RPC throws this status while set.</summary>
    public StatusCode? FailWith { get; set; }

    /// <summary>Watch: throw this after the INITIAL frame.</summary>
    public StatusCode? WatchFailsWith { get; set; }

    /// <summary>Watch: stay open after INITIAL until the client goes away.</summary>
    public bool WatchHangs { get; set; }

    /// <summary>Watch: the action sent with the second frame.</summary>
    public string ModifiedAction { get; set; } = "MODIFIED";

    /// <summary>Set once a watch has written its INITIAL frame.</summary>
    public TaskCompletionSource WatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Set when a hanging watch sees its call cancelled (the client, or Skanyxx, went away).</summary>
    public TaskCompletionSource WatchCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>While set, ListTasks waits on it (or on its call's cancellation).</summary>
    public TaskCompletionSource? HoldLists { get; set; }

    public int ListCalls;

    /// <summary>ListTasks drops these from its pages, as AX drops records it cannot read — so a page can be short mid-list.</summary>
    public HashSet<string> Unlistable { get; } = [];

    /// <summary>Watch: the MODIFIED frame carries a timestamp Skanyxx cannot map (a non-gRPC failure mid-stream).</summary>
    public bool ModifiedIsCorrupt { get; set; }

    /// <summary>While set, UpdateTask waits on it after signalling <see cref="UpdateEntered"/>.</summary>
    public TaskCompletionSource? HoldUpdates { get; set; }

    public TaskCompletionSource UpdateEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AxTask Seed(string name, string? owner, string phase = "Running")
    {
        var task = new AxTask
        {
            ApiVersion = "ax.io/v1alpha1", Kind = "Task",
            Metadata = new ObjectMeta { Name = name, Atespace = "skanyxx", CreationTimestamp = Timestamp.FromDateTime(DateTime.UtcNow) },
            Spec = new TaskSpec { Image = "ghcr.io/acme/agent:1", Command = { "agent", "--goal", "x" } },
            Status = new TaskStatus { Phase = phase }
        };
        if (owner is not null)
            task.Spec.Env.Add(new EnvVar { Name = "SKANYXX_OWNER", Value = owner });
        _tasks[name] = task;
        return task;
    }

    public void SeedModel(string name) => _models[name] = new Model
    {
        Metadata = new ObjectMeta { Name = name },
        Spec = new ModelSpec { Provider = "google", Model = "gemini-2.5-pro", SecretKey = new SecretKeyRef { Name = "gemini", Key = "KEY" } }
    };

    public void Remove(string name) => _tasks.TryRemove(name, out _);

    public AxTask? Stored(string name) => _tasks.TryGetValue(name, out var t) ? t : null;

    private void Enter(string atespace)
    {
        _atespaces.Enqueue(atespace);
        if (FailWith is { } status)
            throw new RpcException(new Status(status, SecretDetail));
    }

    private AxTask Existing(string name) =>
        _tasks.TryGetValue(name, out var task) ? task.Clone() : throw new RpcException(new Status(StatusCode.NotFound, $"task {name} not found"));

    public override Task<AxTask> GetTask(GetTaskRequest request, ServerCallContext context)
    {
        Enter(request.Atespace);
        return Task.FromResult(Existing(request.Name));
    }

    public override async Task<ListTasksResponse> ListTasks(ListTasksRequest request, ServerCallContext context)
    {
        Enter(request.Atespace);
        Interlocked.Increment(ref ListCalls);
        if (HoldLists is { } hold)
            await hold.Task.WaitAsync(context.CancellationToken);
        var limit = request.Limit == 0 ? 50 : (int)request.Limit;
        return new ListTasksResponse
        {
            Tasks =
            {
                _tasks.Values.OrderBy(t => t.Metadata.Name, StringComparer.Ordinal).Skip((int)request.Offset).Take(limit)
                    .Where(t => !Unlistable.Contains(t.Metadata.Name)).Select(t => t.Clone())
            }
        };
    }

    public override async Task<AxTask> UpdateTask(UpdateTaskRequest request, ServerCallContext context)
    {
        Enter(request.Task.Metadata.Atespace);
        if (HoldUpdates is { } hold)
        {
            UpdateEntered.TrySetResult();
            await hold.Task.WaitAsync(context.CancellationToken);
        }
        lock (Updates)
            Updates.Add(request.Clone());
        var task = request.Task.Clone();
        task.Metadata.CreationTimestamp = Timestamp.FromDateTime(DateTime.UtcNow);
        task.Status = new TaskStatus { Phase = task.Spec.Suspend ? "Suspended" : "Pending" };
        _tasks[task.Metadata.Name] = task;
        return task.Clone();
    }

    /// <summary>DeleteTask refuses these (Unavailable), as a task whose deletion AX cannot carry out.</summary>
    public HashSet<string> Undeletable { get; } = [];

    /// <summary>DeleteTask answers NotFound for these: deleted by someone else between the caller's read and its delete.</summary>
    public HashSet<string> GoneOnDelete { get; } = [];

    public override Task<DeleteTaskResponse> DeleteTask(DeleteTaskRequest request, ServerCallContext context)
    {
        Enter(request.Atespace);
        if (Undeletable.Contains(request.Name))
            throw new RpcException(new Status(StatusCode.Unavailable, SecretDetail));
        if (GoneOnDelete.Contains(request.Name))
            throw new RpcException(new Status(StatusCode.NotFound, "task not found"));
        var task = Existing(request.Name);
        Interlocked.Increment(ref DeleteCalls);
        task.Status.Phase = "Terminating";
        _tasks[request.Name] = task;
        return Task.FromResult(new DeleteTaskResponse());
    }

    public override Task<AxTask> SuspendTask(SuspendTaskRequest request, ServerCallContext context) =>
        SetSuspended(request.Atespace, request.Name, true);

    public override Task<AxTask> ResumeTask(ResumeTaskRequest request, ServerCallContext context) =>
        SetSuspended(request.Atespace, request.Name, false);

    private Task<AxTask> SetSuspended(string atespace, string name, bool suspend)
    {
        Enter(atespace);
        var task = Existing(name);
        task.Spec.Suspend = suspend;
        task.Status.Phase = suspend ? "Suspended" : "Running";
        _tasks[name] = task;
        return Task.FromResult(task.Clone());
    }

    public override async Task WatchTask(WatchTaskRequest request, IServerStreamWriter<WatchTaskResponse> responseStream, ServerCallContext context)
    {
        Enter(request.Atespace);
        await responseStream.WriteAsync(new WatchTaskResponse { Task = Existing(request.Name), Action = "INITIAL" });
        WatchStarted.TrySetResult();
        if (WatchFailsWith is { } status)
            throw new RpcException(new Status(status, SecretDetail));
        if (WatchHangs)
        {
            await using var _ = context.CancellationToken.Register(() => WatchCancelled.TrySetResult());
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
            return;
        }

        var running = Existing(request.Name);
        running.Status.Phase = "Running";
        if (ModifiedIsCorrupt)
            running.Metadata.CreationTimestamp = new Timestamp { Seconds = long.MaxValue };
        _tasks[request.Name] = running;
        await responseStream.WriteAsync(new WatchTaskResponse { Task = running.Clone(), Action = ModifiedAction });
    }

    public override Task<ListWorkspacesResponse> ListWorkspaces(ListWorkspacesRequest request, ServerCallContext context)
    {
        Enter(request.Atespace);
        return Task.FromResult(new ListWorkspacesResponse { Workspaces = { _workspaces.Values.Select(w => w.Clone()) } });
    }

    public override Task<Workspace> UpdateWorkspace(UpdateWorkspaceRequest request, ServerCallContext context)
    {
        Enter(request.Workspace.Metadata.Atespace);
        lock (WorkspaceUpdates)
            WorkspaceUpdates.Add(request.Clone());
        _workspaces[request.Workspace.Metadata.Name] = request.Workspace.Clone();
        return Task.FromResult(request.Workspace.Clone());
    }

    public override Task<ListModelsResponse> ListModels(ListModelsRequest request, ServerCallContext context)
    {
        Enter(request.Atespace);
        return Task.FromResult(new ListModelsResponse { Models = { _models.Values.Select(m => m.Clone()) } });
    }
}
