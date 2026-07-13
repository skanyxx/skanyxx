namespace Skanyxx.Core.Infrastructure;

public class PollyRateLimitingHandler : DelegatingHandler
{
    private readonly ILogger<PollyRateLimitingHandler>? _logger;
    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private readonly Queue<DateTime> _requestTimestamps;
    private readonly object _lock = new();

    public PollyRateLimitingHandler(
        int permitLimit = 100,
        TimeSpan? window = null,
        ILogger<PollyRateLimitingHandler>? logger = null)
    {
        _permitLimit = permitLimit;
        _window = window ?? TimeSpan.FromMinutes(1);
        _logger = logger;
        _requestTimestamps = new Queue<DateTime>();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;

            // Remove old timestamps outside the window
            while (_requestTimestamps.Count > 0 && _requestTimestamps.Peek() < now - _window)
            {
                _requestTimestamps.Dequeue();
            }

            // Check rate limit
            if (_requestTimestamps.Count >= _permitLimit)
            {
                _logger?.LogWarning("[RateLimit] Request to {Url} was rate limited", request.RequestUri);

                var oldestRequest = _requestTimestamps.Peek();
                var retryAfter = (oldestRequest + _window) - now;

                var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("Rate limit exceeded. Please try again later.")
                };
                response.Headers.Add("Retry-After", ((int)Math.Max(1, retryAfter.TotalSeconds)).ToString());

                return response;
            }

            _requestTimestamps.Enqueue(now);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
