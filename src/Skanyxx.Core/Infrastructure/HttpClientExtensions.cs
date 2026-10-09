using Skanyxx.Core.Services;

namespace Skanyxx.Core.Infrastructure;

public static class HttpClientExtensions
{
    /// <summary>
    /// One <see cref="KAgentApiClient"/> for the process, on a plain named client: no retry, no circuit breaker, no
    /// client-wide timeout. Never add a retry policy here: A2A <c>message/send</c> and the ModelConfig writes are not
    /// idempotent, so a replay would run a second LLM turn (and its memory writes) or a second save. Timeouts are per
    /// call inside the client (<c>KAgent:ControlTimeoutSeconds</c>, <c>KAgent:ChatTimeoutSeconds</c>).
    /// </summary>
    public static IServiceCollection AddKAgentHttpClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(KAgentApiClient.HttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "SkanyxxWeb/1.0");
        });
        services.AddSingleton(sp => new KAgentApiClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(KAgentApiClient.HttpClientName),
            configuration,
            sp.GetRequiredService<ILogger<KAgentApiClient>>()));
        return services;
    }
}
