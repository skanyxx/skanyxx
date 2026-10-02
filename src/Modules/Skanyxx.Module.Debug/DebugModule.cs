using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core;

namespace Skanyxx.Module.Debug;

public class DebugModule : IModule
{
    public string ModuleId => "debug";
    public string DisplayName => "Debug";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => new[] { "agents", "alerts" };

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) { }
    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
