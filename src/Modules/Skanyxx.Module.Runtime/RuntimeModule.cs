using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core;

namespace Skanyxx.Module.Runtime;

/// <summary>The owner's model step: kagent's ModelConfig through kagent's API (<c>/api/model</c>, D099).</summary>
public class RuntimeModule : IModule
{
    public string ModuleId => "runtime";
    public string DisplayName => "Runtime";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => [];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) { }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
