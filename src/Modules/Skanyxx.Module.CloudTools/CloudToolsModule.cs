using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core;
using Skanyxx.Core.Interfaces;
using Skanyxx.Module.CloudTools.Services;

namespace Skanyxx.Module.CloudTools;

public class CloudToolsModule : IModule
{
    public string ModuleId => "cloudtools";
    public string DisplayName => "Cloud Tools";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IKubernetesService, KubernetesCommandService>();
        services.AddSingleton<ICloudProviderService, CloudCommandService>();
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
