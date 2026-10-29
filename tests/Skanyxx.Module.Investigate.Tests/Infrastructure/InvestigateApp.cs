using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Investigate.Agents;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Tests.Infrastructure;

/// <summary>
/// The investigate module on real Kestrel and Postgres, wired like the Host, with kagent, identity and memory faked.
/// </summary>
public sealed class InvestigateApp : IAsyncDisposable
{
    public const string Owner = "olga";
    public const string Supervisor = "sam";
    public const string Builder = "bea";
    public const string OtherBuilder = "ben";
    public const string Employee = "eli";
    public const string OtherSupervisor = "sue";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static readonly Dictionary<string, string> RolesOf = new()
    {
        [Owner] = SkanyxxRoles.Owner, [Supervisor] = SkanyxxRoles.Supervisor, [Builder] = SkanyxxRoles.Builder,
        [OtherBuilder] = SkanyxxRoles.Builder, [Employee] = SkanyxxRoles.Employee,
        [OtherSupervisor] = SkanyxxRoles.Supervisor
    };

    private readonly WebApplication _app;

    private InvestigateApp(WebApplication app, ScriptedAgents agents, AuditLog audit, FakeCards cards, ErrorLog errors, FakeOrg org, FakeAgentMemory memory) =>
        (_app, Agents, Audit, Cards, _errors, Org, Memory) = (app, agents, audit, cards, errors, org, memory);

    private readonly ErrorLog _errors;

    /// <summary>Every Error-level log line of the app.</summary>
    public IReadOnlyList<string> Errors => _errors.Entries;

    public ScriptedAgents Agents { get; }
    public AuditLog Audit { get; }
    public FakeCards Cards { get; }
    public FakeOrg Org { get; }
    public FakeAgentMemory Memory { get; }

    /// <summary>The app's services, for a test that reaches the database or the worker directly.</summary>
    public IServiceProvider Services => _app.Services;
    private Uri BaseAddress => new(_app.Urls.First());

    public static async Task<InvestigateApp> StartAsync(
        string connectionString, Action<Dictionary<string, string?>>? configure = null, Action<IServiceCollection>? services = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        var errors = new ErrorLog();
        builder.Logging.AddProvider(errors);
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Memory"] = connectionString,
            ["KAgent:IngressUrl"] = "http://127.0.0.1:1",
            ["Investigate:PollSeconds"] = "1",
            ["Investigate:WatchMilliseconds"] = "100",
            ["Investigate:StepTimeoutSeconds"] = "30"
        };
        configure?.Invoke(settings);
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var agents = new ScriptedAgents();
        var org = new FakeOrg();
        var audit = new AuditLog();
        var cards = new FakeCards();
        var memory = new FakeAgentMemory();
        var module = new InvestigateModule();
        module.RegisterServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton<IInvestigationAgents>(agents);
        builder.Services.AddSingleton<IInvestigationAgentClient>(agents);
        builder.Services.AddSingleton<IOrgMembership>(org);
        builder.Services.AddSingleton<IPersonStanding>(org);
        builder.Services.AddSingleton<IRequestHandler<ListTeamsQuery, Outcome<IReadOnlyList<TeamDto>>>>(org);
        builder.Services.AddSingleton<IRequestHandler<PersonTeamsQuery, Outcome<IReadOnlyList<TeamDto>>>>(org);
        builder.Services.AddSingleton<IRequestHandler<ListPeopleQuery, Outcome<IReadOnlyList<PersonDto>>>>(org);
        builder.Services.AddSingleton<IRequestHandler<RecordAuditCommand>>(audit);
        builder.Services.AddSingleton<ICardExtractor>(cards);
        builder.Services.AddSingleton<IAgentMemoryAccess>(memory);
        services?.Invoke(builder.Services);
        builder.Services.AddSkanyxxPlatform([typeof(InvestigateModule).Assembly]);
        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
        builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        // As in the Host: CORS is on, and the endpoints' DisableCors is what keeps other origins out.
        builder.Services.AddCors(o => o.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

        var app = builder.Build();
        app.UseSkanyxxErrorHandling("/Error");
        app.UseRouting();
        app.UseCors("AllowAll");
        app.UseAuthentication();
        app.UseAuthorization();
        app.Map("/Error", () => Results.Content("<html>error page</html>", "text/html")).AllowAnonymous();
        app.UseSkanyxxPlatform([module]);
        await app.StartAsync();
        return new InvestigateApp(app, agents, audit, cards, errors, org, memory);
    }

    /// <summary>Signed in as one of the test people, with their role; null is anonymous.</summary>
    public HttpClient Client(string? userId = Builder)
    {
        var client = new HttpClient { BaseAddress = BaseAddress };
        if (userId is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, RolesOf[userId]);
        }
        return client;
    }

    public static WorkflowStep Agent(string id, string agent, string prompt, string? title = null) =>
        new() { Id = id, Title = title ?? id, Kind = StepKind.Agent, Agent = agent, Prompt = prompt };

    public static WorkflowStep Group(string id, GroupMode mode, params WorkflowStep[] steps) =>
        new() { Id = id, Title = id, Kind = StepKind.Group, Mode = mode, Steps = [.. steps] };

    public async Task<WorkflowDto> CreateAsync(string user, string name, IEnumerable<WorkflowStep> steps, string? sharedTeam = null)
    {
        var response = await Client(user).PostAsJsonAsync("/api/investigate/workflows",
            new { name, description = "", sharedTeam, steps }, Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()} {string.Join("\n", Errors)}");
        return (await response.Content.ReadFromJsonAsync<WorkflowDto>(Json))!;
    }

    public async Task<RunDto> StartAsync(string user, Guid workflowId, string input)
    {
        var response = await Client(user).PostAsJsonAsync("/api/investigate/runs", new { workflowId, input }, Json);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RunDto>(Json))!;
    }

    public async Task<RunDto> GetRunAsync(Guid id, string user = Owner) =>
        (await Client(user).GetFromJsonAsync<RunDto>($"/api/investigate/runs/{id}", Json))!;

    /// <summary>Polls until the run is in one of <paramref name="states"/>; fails loudly with the last state seen.</summary>
    public async Task<RunDto> WaitForAsync(Guid id, params RunState[] states)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        RunDto run;
        do
        {
            run = await GetRunAsync(id);
            if (states.Contains(run.State))
                return run;
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);
        throw new TimeoutException($"run {id} is {run.State}, expected {string.Join("|", states)}");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
