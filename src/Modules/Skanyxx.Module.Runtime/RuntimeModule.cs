using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core;

namespace Skanyxx.Module.Runtime;

/// <summary>Models: kagent's ModelConfigs listed, created, edited and deleted through kagent's API (<c>/api/models</c>, D099, D323).</summary>
public class RuntimeModule : IModule
{
    public string ModuleId => "runtime";
    public string DisplayName => "Runtime";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => [];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) { }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
