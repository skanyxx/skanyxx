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
            // The pages format money with it (Intl currency style): anything but an ISO 4217 code would break them.
            .Validate(o => o.Currency.Length == 3 && o.Currency.All(char.IsAsciiLetterUpper), "Analytics:Currency must be an ISO 4217 code, such as USD.")
            .ValidateOnStart();
        services.AddSingleton(sp => new CostCalculator(sp.GetRequiredService<IOptions<PricingOptions>>().Value));
        return services;
    }
}
