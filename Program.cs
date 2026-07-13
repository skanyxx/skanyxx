using SkanyxxWeb.Interfaces;
using SkanyxxWeb.Infrastructure;
using WebEssentials.AspNetCore.Pwa;
using SkanyxxWeb.Services;
using SkanyxxWeb.Data;
using SkanyxxWeb.Hubs;
using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;
using Asp.Versioning;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Add SQLite database
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "skanyxx.db");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"), ServiceLifetime.Scoped);

// Add Settings service
builder.Services.AddScoped<ISettingsService, SettingsService>();

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();

// Add API Versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("X-Api-Version")
    );
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// Add Swagger/OpenAPI
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Skanyxx API",
        Version = "v1",
        Description = "SRE Platform API for Kubernetes management, agent orchestration, and monitoring",
        Contact = new OpenApiContact
        {
            Name = "Skanyxx",
            Email = "admin@skanyxx.dev"
        }
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Add Health Checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Application is running"));

// Register command execution service
builder.Services.AddSingleton<ICommandExecutionService, CommandExecutionService>();

// Register KAgent API client with Polly resilience and rate limiting
builder.Services.AddKAgentHttpClient(builder.Configuration);
builder.Services.AddSingleton<KAgentApiClient>();

// Register real services that use KAgent API and command execution
builder.Services.AddSingleton<IKubernetesService, KubernetesCommandService>();
builder.Services.AddSingleton<IAgentService, KAgentAgentService>();
builder.Services.AddSingleton<ICloudProviderService, CloudCommandService>();
builder.Services.AddSingleton<IChatService, KAgentChatService>();
builder.Services.AddSingleton<IAlertService, KAgentAlertService>();
builder.Services.AddSingleton<IMemoryService, KAgentMemoryService>();
builder.Services.AddSingleton<IToolServerService, KAgentToolServerService>();
builder.Services.AddSingleton<IHookService, KAgentHookService>();
builder.Services.AddSingleton<IAnalyticsService, KAgentAnalyticsService>();

// Register DevTools service
builder.Services.AddScoped<DevToolsLlmClient>();
builder.Services.AddScoped<IDevToolsService, DevToolsService>();
builder.Services.AddScoped<DevToolsHybridService>();

// Add PWA support for installable app
// Disabled auto-registration as we have custom serviceworker.js
builder.Services.AddProgressiveWebApp(new PwaOptions
{
    CacheId = "Skanyxx_v1",
    Strategy = ServiceWorkerStrategy.CacheFirstSafe,
    RoutesToPreCache = "/,/Index,/Dashboard,/Agents,/Chat,/Investigate,/CloudTools,/ToolServers,/Analytics,/Memory,/Alerts,/Hooks,/Debug,/Settings,/css/site.css,/js/site.js",
    OfflineRoute = "/Offline",
    RegisterServiceWorker = false,
    RegisterWebmanifest = false
});

// Add SignalR for real-time DevTools chat streaming
builder.Services.AddSignalR();

// Add CORS for API access from other apps
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Ensure database is created and migrated
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Create all DevTools tables if they were added after initial DB creation
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "DevToolsWorkspaces" (
            "Id"               INTEGER NOT NULL CONSTRAINT "PK_DevToolsWorkspaces" PRIMARY KEY AUTOINCREMENT,
            "Name"             TEXT    NOT NULL DEFAULT 'Workspace 1',
            "SortOrder"        INTEGER NOT NULL DEFAULT 0,
            "LlmProvider"      TEXT    NOT NULL DEFAULT 'claude',
            "LlmUrl"           TEXT    NOT NULL DEFAULT '',
            "LlmModel"         TEXT    NOT NULL DEFAULT 'claude-sonnet-4-6',
            "ApiKey"           TEXT    NULL,
            "EnabledSkillsJson" TEXT   NOT NULL DEFAULT '[]',
            "JiraUrl"          TEXT    NULL,
            "JiraEmail"        TEXT    NULL,
            "JiraToken"        TEXT    NULL,
            "JiraProjectKey"   TEXT    NULL,
            "MondayToken"      TEXT    NULL,
            "MondayBoardId"    TEXT    NULL,
            "GitRepoPath"      TEXT    NULL,
            "CreatedAt"        TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00',
            "UpdatedAt"        TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00'
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "DevToolsSessions" (
            "Id"              INTEGER NOT NULL CONSTRAINT "PK_DevToolsSessions" PRIMARY KEY AUTOINCREMENT,
            "WorkspaceId"     INTEGER NOT NULL DEFAULT 0,
            "TaskId"          TEXT    NOT NULL DEFAULT '',
            "TaskSource"      TEXT    NOT NULL DEFAULT 'manual',
            "TaskTitle"       TEXT    NOT NULL DEFAULT '',
            "TaskDescription" TEXT    NOT NULL DEFAULT '',
            "MessagesJson"    TEXT    NOT NULL DEFAULT '[]',
            "SummaryContext"  TEXT    NULL,
            "TotalTokensUsed" INTEGER NOT NULL DEFAULT 0,
            "CreatedAt"       TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00',
            "UpdatedAt"       TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00'
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "DevToolsLlmConnections" (
            "Id"        INTEGER NOT NULL CONSTRAINT "PK_DevToolsLlmConnections" PRIMARY KEY AUTOINCREMENT,
            "Name"      TEXT    NOT NULL DEFAULT '',
            "Provider"  TEXT    NOT NULL DEFAULT 'claude',
            "BaseUrl"   TEXT    NOT NULL DEFAULT '',
            "Model"     TEXT    NOT NULL DEFAULT '',
            "ApiKey"    TEXT    NULL,
            "IsDefault" INTEGER NOT NULL DEFAULT 0,
            "IsEnabled" INTEGER NOT NULL DEFAULT 1,
            "CreatedAt" TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00'
        )
        """);

    // Seed default Claude connection if none exist
    if (!db.DevToolsLlmConnections.Any())
    {
        db.DevToolsLlmConnections.Add(new SkanyxxWeb.Models.DevToolsLlmConnection
        {
            Name = "Claude (Anthropic)",
            Provider = "claude",
            Model = "claude-sonnet-4-6",
            IsDefault = true,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    // Create DevToolsSkills table if it was added after initial DB creation
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "DevToolsSkills" (
            "Id"                    INTEGER NOT NULL CONSTRAINT "PK_DevToolsSkills" PRIMARY KEY AUTOINCREMENT,
            "Name"                  TEXT    NOT NULL DEFAULT '',
            "SystemPromptAddition"  TEXT    NOT NULL DEFAULT '',
            "IsBuiltIn"             INTEGER NOT NULL DEFAULT 0,
            "CreatedAt"             TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00'
        )
        """);

    // Seed built-in skills once
    if (!db.DevToolsSkills.Any())
    {
        db.DevToolsSkills.AddRange(DevToolsSkillSeed.BuiltInSkills());
        db.SaveChanges();
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Skanyxx API V1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "Skanyxx API";
    });
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseCors("AllowAll");

app.UseAuthorization();

// Health check endpoint
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapRazorPages();
app.MapControllers();
app.MapHub<DevToolsHub>("/hubs/devtools");

app.Run();
