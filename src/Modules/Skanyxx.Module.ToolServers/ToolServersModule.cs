using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core;
using Skanyxx.Core.Interfaces;
using Skanyxx.Module.ToolServers.Services;

namespace Skanyxx.Module.ToolServers;

public class ToolServersModule : IModule
{
    public string ModuleId => "toolservers";
    public string DisplayName => "Tool Servers";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IToolServerService, KAgentToolServerService>();
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
