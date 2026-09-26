using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Contracts;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Tests.Infrastructure;

/// <summary>The tickets module on real Kestrel, wired like the Host, pointed at a fake kagent.</summary>
public sealed class TicketsApp : IAsyncDisposable
{
    public const string Supervisor = "boss";
    public const string User = "ana";
    private const string AllowAll = "AllowAll";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly WebApplication _app;
    private readonly ErrorLog _errors;

    private TicketsApp(WebApplication app, ErrorLog errors) => (_app, _errors) = (app, errors);

    /// <summary>Every Error-level log line: an expected race must not be reported as a failure.</summary>
    public IReadOnlyList<string> Errors => _errors.Entries;

    public IServiceProvider Services => _app.Services;
    private Uri BaseAddress => new(_app.Urls.First());

    public static async Task<TicketsApp> StartAsync(
        string connectionString, string kagentUrl, Action<IServiceCollection>? services = null, Action<Dictionary<string, string?>>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        var errors = new ErrorLog();
        builder.Logging.AddProvider(errors);
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Tickets"] = connectionString,
            ["KAgent:IngressUrl"] = kagentUrl,
            ["Tickets:Source"] = "local",
            ["Tickets:LocalPath"] = Path.Combine(AppContext.BaseDirectory, "Infrastructure", "tickets.json"),
            ["Tickets:PollSeconds"] = "1",
            ["Tickets:StageTimeoutSeconds"] = "30",
            ["Tickets:Supervisors:0"] = Supervisor,
            ["Tickets:AllowedAgents:0"] = "kagent/ticket-planner",
            ["Tickets:AllowedAgents:1"] = "kagent/ticket-plan-reviewer",
            ["Tickets:AllowedAgents:2"] = "kagent/ticket-coder",
            ["Tickets:AllowedAgents:3"] = "kagent/ticket-qa",
            ["Tickets:AllowedAgents:4"] = "kagent/ticket-reviewer",
            ["Tickets:AllowedAgents:5"] = "kagent/ticket-refiner",
            ["Tickets:AllowedAgents:6"] = "kagent/ticket-boom"
        };
        configure?.Invoke(settings);
        // Only these settings: the Host's appsettings.json is copied into this bin by the project reference.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var module = new TicketsModule();
        module.RegisterServices(builder.Services, builder.Configuration);
        services?.Invoke(builder.Services);
        builder.Services.AddSkanyxxPlatform([typeof(TicketsModule).Assembly]);
        builder.Services.AddCors(o => o.AddPolicy(AllowAll, p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

        var app = builder.Build();
        app.UseSkanyxxErrorHandling("/Error");
        app.UseRouting();
        app.UseCors(AllowAll);
        app.Map("/Error", () => Results.Content("<html>error page</html>", "text/html"));
        app.UseSkanyxxPlatform([module]);
        await app.StartAsync();
        return new TicketsApp(app, errors);
    }

    public HttpClient Client(string? userId = User)
    {
        var client = new HttpClient { BaseAddress = BaseAddress };
        if (userId is not null)
            client.DefaultRequestHeaders.Add("X-User-Id", userId);
        return client;
    }

    public async Task<RunDto> StartRunAsync(string ticketKey = "SDB-1", string pipelineId = "ticket-fix")
    {
        var response = await Client().PostAsJsonAsync("/api/tickets/runs", new { ticketKey, pipelineId });
        Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RunDto>(Json))!;
    }

    public async Task<RunDto> GetRunAsync(Guid id) =>
        (await Client().GetFromJsonAsync<RunDto>($"/api/tickets/runs/{id}", Json))!;

    /// <summary>Polls until the run reaches one of <paramref name="states"/>; fails loudly with the last state seen.</summary>
    public async Task<RunDto> WaitForAsync(Guid id, params RunState[] states)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        RunDto run;
        do
        {
            run = await GetRunAsync(id);
            if (states.Contains(run.State))
                return run;
            await Task.Delay(100);
        } while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"run {id} is {run.State} at cursor {run.Cursor}, expected {string.Join("|", states)}");
    }

    public Task<HttpResponseMessage> DecideAsync(Guid id, string decision, string? note = null, string userId = User) =>
        Client(userId).PostAsJsonAsync($"/api/tickets/runs/{id}/decision", new { decision, note });

    public Task<HttpResponseMessage> SavePipelineAsync(string id, object pipeline, string userId = Supervisor) =>
        Client(userId).PutAsJsonAsync($"/api/tickets/pipelines/{id}", pipeline);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
