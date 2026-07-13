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
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

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
using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var moduleLoader = new ModuleLoader(builder.Configuration, loggerFactory.CreateLogger<ModuleLoader>());
moduleLoader.DiscoverModules(builder.Environment.ContentRootPath);

foreach (var asm in moduleLoader.GetModuleAssemblies())
    mvcBuilder.AddApplicationPart(asm);

moduleLoader.RegisterAllModuleServices(builder.Services, builder.Configuration);

// Register shared services
builder.Services.AddSingleton<ICommandExecutionService, CommandExecutionService>();
builder.Services.AddKAgentHttpClient(builder.Configuration);
builder.Services.AddSingleton<KAgentApiClient>();

// Add MediatR scanning all assemblies
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(IModule).Assembly);
    foreach (var asm in moduleLoader.GetModuleAssemblies())
        cfg.RegisterServicesFromAssembly(asm);
});

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

// Add PWA support
builder.Services.AddProgressiveWebApp(new PwaOptions
{
    CacheId = "Skanyxx_v1",
    Strategy = ServiceWorkerStrategy.CacheFirstSafe,
    RoutesToPreCache = "/,/Index,/Dashboard,/Agents,/Chat,/Investigate,/CloudTools,/ToolServers,/Analytics,/Memory,/Alerts,/Hooks,/Debug,/Settings,/css/site.css,/js/site.js",
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
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseCors("AllowAll");

app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapRazorPages();
app.MapControllers();

app.Run();
