using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Core.Platform;

/// <summary>
/// An API change that was saved but whose revocation did not finish answers <c>500</c> with what did not happen and how
/// to retry (<see cref="RevocationFailedException"/>, D162), instead of the bare error. Pages handle it themselves.
/// </summary>
internal sealed class RevocationFailedProblem(IProblemDetailsService problems) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not RevocationFailedException failed || !PlatformExtensions.IsApi(context.Request.Path))
            return ValueTask.FromResult(false);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "The change was saved, but not everything it revokes was revoked.",
                Detail = failed.Message
            }
        });
    }
}
