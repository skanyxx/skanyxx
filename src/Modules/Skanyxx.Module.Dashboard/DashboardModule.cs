using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core;

namespace Skanyxx.Module.Dashboard;

public class DashboardModule : IModule
{
    public string ModuleId => "dashboard";
    public string DisplayName => "Dashboard";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => new[] { "agents", "alerts", "analytics", "cloudtools" };

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) { }
    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
