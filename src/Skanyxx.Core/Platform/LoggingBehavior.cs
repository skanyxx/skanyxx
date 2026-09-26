using System.Diagnostics;
using MediatR;

namespace Skanyxx.Core.Platform;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var response = await next();
        logger.LogInformation("Handled {Request} in {ElapsedMs:0.0} ms",
            typeof(TRequest).Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return response;
    }
}
