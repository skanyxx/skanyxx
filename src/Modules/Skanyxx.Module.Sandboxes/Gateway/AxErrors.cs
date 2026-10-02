using FluentValidation;
using FluentValidation.Results;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Sandboxes.Gateway;

/// <summary>
/// gRPC status → HTTP, with a fixed message per category: AX's own error text can carry its address or internals,
/// so it is logged, never returned. A caller that went away is not an AX failure: gRPC reports it as
/// <c>Cancelled</c>, which becomes an unlogged <see cref="OperationCanceledException"/> and no response.
/// </summary>
internal static class AxErrors
{
    public const string Unreachable = "AX unreachable.";
    public const string Failed = "AX request failed.";
    public const string Refused = "AX refused the change in the task's current state.";

    public static async Task<Outcome<T>> Guard<T>(ILogger logger, CancellationToken ct, Func<Task<Outcome<T>>> call)
    {
        try
        {
            return await call();
        }
        catch (RpcException ex) when (IsCancelled(ex, ct))
        {
            throw new OperationCanceledException("The caller went away.", ex, ct);
        }
        catch (RpcException ex)
        {
            logger.LogWarning("AX call failed with {StatusCode}: {Detail}", ex.StatusCode, ex.Status.Detail);
            return ex.StatusCode switch
            {
                StatusCode.NotFound => Outcome<T>.NotFound("Not found in AX."),
                StatusCode.AlreadyExists => Outcome<T>.Conflict(default, "Already exists in AX."),
                StatusCode.FailedPrecondition or StatusCode.Aborted => Outcome<T>.Conflict(default, Refused),
                StatusCode.InvalidArgument => throw new ValidationException([new ValidationFailure("ax", "AX rejected the request.")]),
                _ => Outcome<T>.Unavailable(Category(ex))
            };
        }
    }

    /// <summary>gRPC's own cancellation of a call whose token (the caller's, or one linked to it) fired.</summary>
    public static bool IsCancelled(Exception ex, CancellationToken token) =>
        token.IsCancellationRequested && ex is OperationCanceledException or RpcException { StatusCode: StatusCode.Cancelled };

    public static string Category(RpcException ex) =>
        ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded ? Unreachable : Failed;
}
