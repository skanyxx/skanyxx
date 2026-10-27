using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skanyxx.Core;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.Chat.Features;

namespace Skanyxx.Module.Chat;

/// <summary>People talk to merged kagent agents here (D017): <c>GET api/chat/agents</c>, <c>POST api/chat</c>.</summary>
public class ChatModule : IModule
{
    public string ModuleId => "chat";
    public string DisplayName => "Chat";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();

    // TryAdd: the chatcontext module's registration wins whichever registers first (D236).
    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services.TryAddScoped<IChatContext, NoChatContext>();

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
