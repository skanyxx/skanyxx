using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core;

namespace Skanyxx.Module.Investigate;

public class InvestigateModule : IModule
{
    public string ModuleId => "investigate";
    public string DisplayName => "Investigate";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => new[] { "agents", "chat" };

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) { }
    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
