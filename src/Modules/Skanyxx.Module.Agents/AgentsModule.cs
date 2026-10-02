using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core;
using Skanyxx.Core.Interfaces;
using Skanyxx.Module.Agents.Services;

namespace Skanyxx.Module.Agents;

public class AgentsModule : IModule
{
    public string ModuleId => "agents";
    public string DisplayName => "Agents";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAgentService, KAgentAgentService>();
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
