using Skanyxx.Core;
using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Infrastructure;
using Skanyxx.Core.Services;
using Skanyxx.Host;
using Skanyxx.Host.Data;
using WebEssentials.AspNetCore.Pwa;
using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;
using Asp.Versioning;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using Skanyxx.Core.Platform;

// The entry point only turns a failure into an exit code: a rethrow ends in abort(), which a container's PID 1 ignores,
// so the process stayed alive (spinning) instead of crash-looping (slice 4, D135/D146).
try
{
    await Program.RunAsync(args);
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException) // HostAbortedException: EF tooling stopping the host on purpose.
{
    Log.Fatal(ex, "Host terminated during startup or run");
    return 1;
}
finally
{
    // Flush buffered log events (e.g. why the host failed to start) before the process exits.
    Log.CloseAndFlush();
}

public partial class Program
{
    /// <summary>Builds and runs the host; a startup failure throws (tests call this to see the exception).</summary>
    public static async Task RunAsync(string[] args)
    {
        // Bootstrap logger: module discovery logs before the host (and its configured Serilog) exists.
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console(new RenderedCompactJsonFormatter())
            .CreateBootstrapLogger();

        var builder = WebApplication.CreateBuilder(args);
        HostFilteringGuard.EnsureAllowedHosts(builder.Configuration, builder.Environment);

        builder.Host.UseSerilog((context, logger) => logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            // HttpClient logs every request URI at Information; a Jira search URI carries the JQL.
            .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
            // The health service logs every failing check with its stack trace; /health logs one warning per call instead.
            .MinimumLevel.Override("Microsoft.Extensions.Diagnostics.HealthChecks", Serilog.Events.LogEventLevel.Fatal)
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console(new RenderedCompactJsonFormatter()));

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("skanyxx"))
            .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddNpgsql())
            .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
        // Export only when a collector is configured (standard OTEL_EXPORTER_OTLP_ENDPOINT variable).
        if (!string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            telemetry.UseOtlpExporter();

        // Add SQLite database
        var dbPath = Path.Combine(builder.Environment.ContentRootPath, "skanyxx.db");
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Register DbContext base type so modules can resolve it without referencing Host
        builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Add services to the container.
        builder.Services.AddRazorPages();
        var mvcBuilder = builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddHttpClient();

        // Module Loading
        using var loggerFactory = new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger);
        var moduleLoader = new ModuleLoader(builder.Configuration, loggerFactory.CreateLogger<ModuleLoader>());
        moduleLoader.DiscoverModules(builder.Environment.ContentRootPath);

        foreach (var asm in moduleLoader.GetModuleAssemblies())
            mvcBuilder.AddApplicationPart(asm);

        moduleLoader.RegisterAllModuleServices(builder.Services, builder.Configuration);

        // Register shared services
        builder.Services.AddSingleton<ICommandExecutionService, CommandExecutionService>();
        builder.Services.AddKAgentHttpClient(builder.Configuration);

        // MediatR (+ validation/logging pipeline), validators and FastEndpoints over Core + module assemblies
        builder.Services.AddSkanyxxPlatform(moduleLoader.GetModuleAssemblies());
        builder.Services.AddSkanyxxApiGuards(builder.Configuration).AddSkanyxxHealthTimeouts();
        // Cookie (browser) + bearer (API/desktop) over the identity module's store; everything requires sign-in by default.
        builder.Services.AddSkanyxxAuthentication(builder.Environment);

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
                Description = "Bearer access token from POST /api/identity/sign-in (not a JWT)",
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

        // Add PWA support
        builder.Services.AddProgressiveWebApp(new PwaOptions
        {
            CacheId = "Skanyxx_v1",
            Strategy = ServiceWorkerStrategy.CacheFirstSafe,
            RoutesToPreCache = "/,/Index,/Dashboard,/Agents,/Chat,/Investigate,/CloudTools,/ToolServers,/Analytics,/Alerts,/Hooks,/Debug,/Settings,/css/site.css,/js/site.js",
            OfflineRoute = "/Offline",
            RegisterServiceWorker = false,
            RegisterWebmanifest = false
        });

        // Add CORS
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

        // Ensure database is created
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        }

        // Initialize modules
        await moduleLoader.InitializeAllModulesAsync(app.Services);

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
            // Must stay before UseRouting below: the page branch re-executes "/Error" through routing.
            app.UseSkanyxxErrorHandling("/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseSerilogRequestLogging();
        app.UseSkanyxxApiGuards();

        app.UseRouting();
        app.UseCors("AllowAll");

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapSkanyxxHealth("/health");

        app.MapRazorPages();
        app.MapControllers();
        app.UseSkanyxxPlatform(moduleLoader.GetModules());

        await app.RunAsync();
    }
}
