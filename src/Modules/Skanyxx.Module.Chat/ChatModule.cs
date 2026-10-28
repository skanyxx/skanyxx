using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skanyxx.Core;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Chat.Features;

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
        // Chat works without memory; the memory module's own registrations win when it is loaded.
        services.TryAddScoped<IRelatedMemory, NoRelatedMemory>();
        services.TryAddScoped<ICardExtractor, NoCardExtractor>();
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
