using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core;
using Skanyxx.Core.Interfaces;
using Skanyxx.Module.Chat.Services;

namespace Skanyxx.Module.Chat;

public class ChatModule : IModule
{
    public string ModuleId => "chat";
    public string DisplayName => "Chat";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IChatService, KAgentChatService>();
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
