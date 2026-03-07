using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core;

namespace Skanyxx.Module.Sessions;

public class SessionsModule : IModule
{
    public string ModuleId => "sessions";
    public string DisplayName => "Sessions";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) { }
    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
