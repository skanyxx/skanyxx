using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Skanyxx.Core.Platform.Usage;

/// <summary>The shared token estimate and cost calculator; every module that counts or prices tokens adds them (once).</summary>
public static class UsageServices
{
    public static IServiceCollection AddTokenUsage(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<ITokenEstimator>(CharTokenEstimator.Instance);
        if (services.Any(s => s.ServiceType == typeof(CostCalculator)))
            return services;
        services.AddOptions<PricingOptions>()
            .Bind(configuration.GetSection(PricingOptions.Section))
            .Validate(o => o.Prices.Values.All(p => p.InputPerMillion >= 0 && p.OutputPerMillion >= 0), "Analytics:Prices cannot be negative.")
            .ValidateOnStart();
        services.AddSingleton(sp => new CostCalculator(sp.GetRequiredService<IOptions<PricingOptions>>().Value));
        return services;
    }
}
