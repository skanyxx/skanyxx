using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core;
using Skanyxx.Core.Interfaces;
using Skanyxx.Module.Analytics.Services;

namespace Skanyxx.Module.Analytics;

public class AnalyticsModule : IModule
{
    public string ModuleId => "analytics";
    public string DisplayName => "Analytics";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAnalyticsService, KAgentAnalyticsService>();
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
