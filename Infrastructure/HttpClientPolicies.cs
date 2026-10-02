using Polly;
using Polly.Extensions.Http;
using Polly.RateLimiting;

namespace SkanyxxWeb.Infrastructure;

public static class HttpClientPolicies
{
    /// <summary>
    /// Retry policy: 3 retries with exponential backoff (2s, 4s, 8s)
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TaskCanceledException>()
            .Or<RateLimiterRejectedException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    Console.WriteLine($"[Polly] Retry {retryAttempt} after {timespan.TotalSeconds}s due to: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
                });
    }

    /// <summary>
    /// Circuit breaker: Opens after 5 failures, stays open for 30 seconds
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TaskCanceledException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, breakDelay) =>
                {
                    Console.WriteLine($"[Polly] Circuit breaker opened for {breakDelay.TotalSeconds}s due to: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
                },
                onReset: () => Console.WriteLine("[Polly] Circuit breaker reset"),
                onHalfOpen: () => Console.WriteLine("[Polly] Circuit breaker half-open"));
    }
}
