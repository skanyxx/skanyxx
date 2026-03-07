using Skanyxx.Core.Services;

namespace Skanyxx.Core.Infrastructure;

public static class HttpClientExtensions
{
    public static IServiceCollection AddKAgentHttpClient(this IServiceCollection services, IConfiguration configuration)
    {
        var kagentSection = configuration.GetSection("KAgent");

        // Build URL from config parts
        var protocol = kagentSection["Protocol"] ?? "http";
        var host = kagentSection["BaseUrl"] ?? "localhost";
        var port = kagentSection["Port"] ?? "8083";
        var ingressUrl = kagentSection["IngressUrl"];

        // Use IngressUrl if set, otherwise build from parts
        var baseUrl = !string.IsNullOrEmpty(ingressUrl)
            ? ingressUrl
            : $"{protocol}://{host}:{port}";

        var timeout = int.TryParse(kagentSection["Timeout"], out var t) ? t : 30000;

        services.AddTransient<PollyRateLimitingHandler>();

        services.AddHttpClient<KAgentApiClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromMilliseconds(timeout);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "SkanyxxWeb/1.0");
        })
        .AddHttpMessageHandler<PollyRateLimitingHandler>()
        .AddPolicyHandler(HttpClientPolicies.GetRetryPolicy())
        .AddPolicyHandler(HttpClientPolicies.GetCircuitBreakerPolicy())
        .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        return services;
    }

    public static IHttpClientBuilder AddResilientHttpClient(
        this IServiceCollection services,
        string name,
        Action<HttpClient> configureClient,
        RateLimiterOptions? rateLimiterOptions = null)
    {
        var options = rateLimiterOptions ?? new RateLimiterOptions();

        return services.AddHttpClient(name, configureClient)
            .AddHttpMessageHandler(() => new PollyRateLimitingHandler(options.PermitLimit, options.Window))
            .AddPolicyHandler(HttpClientPolicies.GetRetryPolicy())
            .AddPolicyHandler(HttpClientPolicies.GetCircuitBreakerPolicy())
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));
    }

    public static IHttpClientBuilder AddResilientHttpClient<TClient>(
        this IServiceCollection services,
        Action<HttpClient> configureClient,
        RateLimiterOptions? rateLimiterOptions = null)
        where TClient : class
    {
        var options = rateLimiterOptions ?? new RateLimiterOptions();

        return services.AddHttpClient<TClient>(configureClient)
            .AddHttpMessageHandler(() => new PollyRateLimitingHandler(options.PermitLimit, options.Window))
            .AddPolicyHandler(HttpClientPolicies.GetRetryPolicy())
            .AddPolicyHandler(HttpClientPolicies.GetCircuitBreakerPolicy())
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));
    }
}
