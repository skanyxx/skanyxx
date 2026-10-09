using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core;

namespace Skanyxx.Module.Chat;

/// <summary>People talk to merged kagent agents here (D017): <c>GET api/chat/agents</c>, <c>POST api/chat</c>.</summary>
public class ChatModule : IModule
{
    public string ModuleId => "chat";
    public string DisplayName => "Chat";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
