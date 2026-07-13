using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Skanyxx.Core;

public interface IModule
{
    string ModuleId { get; }
    string DisplayName { get; }
    string Version { get; }
    IReadOnlyList<string> Dependencies { get; }
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
    Task InitializeAsync(IServiceProvider serviceProvider);
}
