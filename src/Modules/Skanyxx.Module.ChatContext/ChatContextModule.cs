using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Skanyxx.Core;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Core.Platform.Runtime;
using Skanyxx.Module.ChatContext.Access;
using Skanyxx.Module.ChatContext.Cache;
using Skanyxx.Module.ChatContext.Context;
using Skanyxx.Module.ChatContext.Data;
using Skanyxx.Module.ChatContext.Extraction;

namespace Skanyxx.Module.ChatContext;

/// <summary>
/// The chat index (D236–D241): every Chat session indexed in Postgres (keywords, categories, tickets, outcome; FTS),
/// related prior work on a new chat's first turn, ticket views, and an optional answer cache. Memory's database; reads
/// cards through memory's library query, so it needs memory (and so identity, for org membership).
/// </summary>
public sealed class ChatContextModule : IModule
{
    public string ModuleId => "chatcontext";
    public string DisplayName => "Chat context";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => ["memory"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ChatContextOptions>()
            .Bind(configuration.GetSection(ChatContextOptions.Section))
            .Configure(o => o.ConnectionString = configuration.GetConnectionString("Memory") ?? "")
            .ValidateDataAnnotations()
            .Validate(o => !o.AnswerCache.Enabled || !string.IsNullOrWhiteSpace(o.AnswerCache.Redis),
                "ChatContext:AnswerCache:Enabled needs ChatContext:AnswerCache:Redis (the Dragonfly/Redis address).")
            .ValidateOnStart();

        services.AddDbContext<ChatContextDbContext>((sp, o) => o.UseNpgsql(
            sp.GetRequiredService<IOptions<ChatContextOptions>>().Value.ConnectionSettings().ConnectionString,
            npgsql => npgsql.MigrationsHistoryTable(ChatContextDbContext.MigrationsTable)));
        services.AddHostedService<ChatContextMigrator>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<SessionSearch>();
        services.AddScoped<SessionAccess>();
        services.TryAddSingleton<IChatExtractor, DeterministicExtractor>();
        // Replace, not add: Chat registers a no-op (TryAdd) so it runs without this module.
        services.Replace(ServiceDescriptor.Scoped<IChatContext, ChatContextService>());

        services.AddSingleton<IAnswerStore, RedisAnswerStore>();
        services.AddSingleton<AnswerCache>();
        if (configuration.GetValue<bool>($"{ChatContextOptions.Section}:AnswerCache:Enabled"))
            services.AddTransient<IPipelineBehavior<SaveModelSettingsCommand, Outcome<ModelSettings>>, ModelChangeClearsAnswers>();

        services.AddHealthChecks().AddNpgSql(
            sp => sp.GetRequiredService<IOptions<ChatContextOptions>>().Value.ConnectionSettings().ConnectionString,
            name: "chatcontext-postgres",
            tags: ["chatcontext"]);
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
