using System.Runtime.CompilerServices;
using Ax.V1Alpha1;
using Grpc.Core;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;
using Task = System.Threading.Tasks.Task;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

/// <summary>
/// Checks the task exists before anything is streamed (so a missing one is a plain 404), then relays AX's watch,
/// with a keep-alive whenever AX is quiet. Once streaming, a failure can no longer change the status code, so it
/// becomes a terminal <c>error</c> frame. A client that goes away cancels the AX stream and is not logged.
/// </summary>
internal sealed class WatchTaskHandler(AxGateway ax, IOptions<SandboxesOptions> options, ILogger<WatchTaskHandler> logger)
    : IRequestHandler<WatchTaskQuery, Outcome<IAsyncEnumerable<SandboxTaskEvent>>>
{
    public Task<Outcome<IAsyncEnumerable<SandboxTaskEvent>>> Handle(WatchTaskQuery query, CancellationToken ct) =>
        AxErrors.Guard(logger, ct, async () => await ax.FindTaskAsync(query.Name, ct) is null
            ? Outcome<IAsyncEnumerable<SandboxTaskEvent>>.NotFound($"No task '{query.Name}'.")
            : Outcome<IAsyncEnumerable<SandboxTaskEvent>>.Ok(StreamAsync(query.Name, ct)));

    private async IAsyncEnumerable<SandboxTaskEvent> StreamAsync(string name, [EnumeratorCancellation] CancellationToken ct)
    {
        var keepAlive = TimeSpan.FromSeconds(options.Value.KeepAliveSeconds);
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(TimeSpan.FromSeconds(options.Value.WatchSeconds));

        var watch = ax.WatchTaskAsync(name, window.Token).GetAsyncEnumerator(window.Token);
        var pending = watch.MoveNextAsync().AsTask();
        try
        {
            while (true)
            {
                if (await Task.WhenAny(pending, Task.Delay(keepAlive, window.Token)) != pending && !window.IsCancellationRequested)
                {
                    yield return new SandboxTaskEvent(WatchEventKinds.KeepAlive);
                    continue;
                }

                WatchTaskResponse? next = null;
                string? error = null;
                try
                {
                    next = await pending ? watch.Current : null;
                }
                catch (Exception ex) when (AxErrors.IsCancelled(ex, window.Token) && !ct.IsCancellationRequested)
                {
                    // The watch window ran out: finish with a fresh read, like a normal close.
                }
                catch (RpcException ex) when (AxErrors.IsCancelled(ex, ct))
                {
                    throw new OperationCanceledException("The caller went away.", ex, ct);
                }
                catch (RpcException ex)
                {
                    logger.LogWarning("AX watch failed with {StatusCode}: {Detail}", ex.StatusCode, ex.Status.Detail);
                    error = AxErrors.Category(ex);
                }

                if (error is not null)
                {
                    yield return new SandboxTaskEvent(WatchEventKinds.Error, Error: error);
                    yield break;
                }
                if (next is null)
                    break;
                yield return new SandboxTaskEvent(KindOf(next.Action), next.Task is null ? null : AxMapper.ToSandboxTask(next.Task));
                pending = watch.MoveNextAsync().AsTask();
            }
        }
        finally
        {
            // The AX stream may still have a read in flight (keep-alive, or the consumer stopped): end it before disposing.
            await window.CancelAsync();
            await Task.WhenAny(pending);
            await watch.DisposeAsync();
        }

        yield return await FinalAsync(name, ct);
    }

    private static string KindOf(string action) => action == "INITIAL" ? WatchEventKinds.Initial : WatchEventKinds.Modified;

    private async Task<SandboxTaskEvent> FinalAsync(string name, CancellationToken ct)
    {
        try
        {
            return await ax.FindTaskAsync(name, ct) is { } task
                ? new SandboxTaskEvent(WatchEventKinds.Final, AxMapper.ToSandboxTask(task))
                : new SandboxTaskEvent(WatchEventKinds.Gone);
        }
        catch (RpcException ex) when (AxErrors.IsCancelled(ex, ct))
        {
            throw new OperationCanceledException("The caller went away.", ex, ct);
        }
        catch (RpcException ex)
        {
            logger.LogWarning("AX read after watch failed with {StatusCode}: {Detail}", ex.StatusCode, ex.Status.Detail);
            return new SandboxTaskEvent(WatchEventKinds.Error, Error: AxErrors.Category(ex));
        }
    }
}
