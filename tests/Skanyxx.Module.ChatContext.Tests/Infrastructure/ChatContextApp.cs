using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Skanyxx.Core;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Tickets;
using Skanyxx.Module.ChatContext.Cache;
using Skanyxx.Module.Memory;

namespace Skanyxx.Module.ChatContext.Tests.Infrastructure;

/// <summary>
/// Memory and chatcontext on real Kestrel, wired like the Host (platform, module hooks, authentication), with test
/// stand-ins for identity's org tree, the tickets module's runs and Redis.
/// </summary>
public sealed class ChatContextApp : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly WebApplication _app;

    private ChatContextApp(WebApplication app) => _app = app;

    public IServiceProvider Services => _app.Services;

    public FakeOrgMembership Org => _app.Services.GetRequiredService<FakeOrgMembership>();

    public FakeTicketRuns Runs => _app.Services.GetRequiredService<FakeTicketRuns>();

    internal FakeAnswerStore Store => _app.Services.GetRequiredService<FakeAnswerStore>();

    public static async Task<ChatContextApp> StartAsync(string connectionString, IDictionary<string, string?>? extra = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:Memory"] = connectionString };
        foreach (var (key, value) in extra ?? new Dictionary<string, string?>())
            settings[key] = value;
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        IModule[] modules = [new MemoryModule(), new ChatContextModule()];
        foreach (var module in modules)
            module.RegisterServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton<FakeOrgMembership>();
        builder.Services.AddSingleton<IOrgMembership>(sp => sp.GetRequiredService<FakeOrgMembership>());
        builder.Services.AddSingleton<FakeTicketRuns>();
        builder.Services.AddSingleton<ITicketRuns>(sp => sp.GetRequiredService<FakeTicketRuns>());
        builder.Services.AddSingleton<FakeAnswerStore>();
        builder.Services.Replace(ServiceDescriptor.Singleton<IAnswerStore>(sp => sp.GetRequiredService<FakeAnswerStore>()));
        builder.Services.AddSkanyxxPlatform([.. modules.Select(m => m.GetType().Assembly)]);
        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
        builder.Services.AddCors();
        builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        var app = builder.Build();
        app.UseSkanyxxErrorHandling("/Error");
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseSkanyxxPlatform(modules);
        await app.StartAsync();
        return new ChatContextApp(app);
    }

    public HttpClient Client(string userId)
    {
        var client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        if (People.Roles(userId) is { Length: > 0 } roles)
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        return client;
    }

    /// <summary>One request's <see cref="IChatContext"/>, as Chat's handler gets it.</summary>
    public async Task<T> WithChatContextAsync<T>(Func<IChatContext, Task<T>> use)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<IChatContext>());
    }

    /// <summary>A completed turn, as Chat reports it.</summary>
    public Task TurnAsync(string userId, string sessionId, string message, string reply, string[]? tickets = null, string agent = "kagent/seed",
        bool first = true, string? cacheKey = null) =>
        WithChatContextAsync<bool>(async context =>
        {
            await context.CompletedAsync(new ChatTurnDone(Person(userId), sessionId, agent, message, reply, first, tickets ?? [], cacheKey), CancellationToken.None);
            return true;
        });

    public Task<ChatPreparation> PrepareAsync(string userId, string message, string[]? tickets = null, string agent = "kagent/seed",
        string revision = "1", bool noCache = false) =>
        WithChatContextAsync(context => context.PrepareAsync(new ChatTurnStart(Person(userId), agent, revision, message, tickets ?? [], noCache), CancellationToken.None));

    public async Task ShareAsync(string userId, string sessionId, string scope) =>
        (await Client(userId).PutAsJsonAsync($"/api/chat/sessions/{sessionId}/scope", new { scope })).EnsureSuccessStatusCode();

    public async Task CardAsync(string userId, string scope, string key, string what, string why) =>
        (await Client(userId).PutAsJsonAsync($"/api/memory/cards/{scope}/{key}", new { version = 0, type = "fact", what, why })).EnsureSuccessStatusCode();

    public async Task<List<JsonElement>> SearchAsync(string userId, string query) =>
        (await Client(userId).GetFromJsonAsync<List<JsonElement>>($"/api/chat/sessions{query}", Json))!;

    public static ChatReader Person(string userId) => userId switch
    {
        People.Boss => People.Supervisor,
        People.Olivia => People.Owner,
        _ => People.Person(userId)
    };

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
