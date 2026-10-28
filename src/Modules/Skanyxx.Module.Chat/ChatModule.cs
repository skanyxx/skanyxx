using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using Skanyxx.Core;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Chat.Features;
using Skanyxx.Module.Chat.Features.Files;
using Skanyxx.Module.Chat.Features.Turns;

namespace Skanyxx.Module.Chat;

/// <summary>
/// People talk to merged kagent agents here (D017): the agent picker and details, conversations (kagent's sessions, as
/// the person), streamed turns, and files attached to a turn.
/// </summary>
public class ChatModule : IModule
{
    public string ModuleId => "chat";
    public string DisplayName => "Chat";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();

    // TryAdd: Chat works without memory, the chat index or a quota module; their registrations win when loaded.
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ChatOptions>()
            .Bind(configuration.GetSection(ChatOptions.Section))
            .ValidateDataAnnotations()
            .Validate<IWebHostEnvironment>(
                (o, env) => env.WebRootPath is null || !ChatFiles.IsUnder(ChatFiles.Root(o.Files, env), env.WebRootPath),
                "Chat:Files:Path must not be under wwwroot: uploaded files are never served as static files.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ChatFiles>();
        services.AddScoped<ChatTurns>();

        services.TryAddScoped<IRelatedMemory, NoRelatedMemory>();
        services.TryAddScoped<ICardExtractor, NoCardExtractor>();
        services.TryAddScoped<IAgentMemoryAccess, NoAgentMemoryAccess>();
        services.TryAddScoped<IChatContext, NoChatContext>();
        services.TryAddScoped<IChatQuota, UnlimitedChatQuota>();
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
