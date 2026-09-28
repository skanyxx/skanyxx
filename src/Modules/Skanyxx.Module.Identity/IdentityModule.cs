using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity;

/// <summary>
/// Local accounts (ASP.NET Core Identity on Postgres): owner bootstrap, password sign-in (cookie or bearer), refresh,
/// sign-out, break-glass unlock. The Host owns the authentication schemes and the fallback policy; this module owns
/// the store and the session rules (D1, D025).
/// </summary>
public sealed class IdentityModule : IModule
{
    public string ModuleId => "identity";
    public string DisplayName => "Identity";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => [];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentityModuleOptions>()
            .Bind(configuration.GetSection(IdentityModuleOptions.Section))
            .Configure(o => o.ConnectionString = configuration.GetConnectionString("Identity") ?? "")
            .ValidateDataAnnotations()
            .Validate(o => string.IsNullOrEmpty(o.BootstrapToken)
                    || (o.BootstrapToken.Trim() == o.BootstrapToken && o.BootstrapToken.Length >= IdentityModuleOptions.MinBootstrapTokenLength),
                $"Identity:BootstrapToken must be at least {IdentityModuleOptions.MinBootstrapTokenLength} characters with no leading or " +
                "trailing whitespace (or empty).")
            .ValidateOnStart();

        services.AddDbContext<AccountsDbContext>((sp, o) => o.UseNpgsql(
            sp.GetRequiredService<IOptions<IdentityModuleOptions>>().Value.ConnectionSettings().ConnectionString,
            npgsql => npgsql.MigrationsHistoryTable(AccountsDbContext.MigrationsTable)));
        services.AddHostedService<IdentityMigrator>();
        AddDataProtection(services, configuration);

        services.AddIdentityCore<IdentityUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AccountsDbContext>()
            .AddSignInManager();
        services.AddOptions<IdentityOptions>().Configure<IOptions<IdentityModuleOptions>>((identity, module) =>
        {
            var o = module.Value;
            identity.User.RequireUniqueEmail = true;
            identity.Password.RequiredLength = o.PasswordMinLength;
            identity.Password.RequireDigit = false;
            identity.Password.RequireLowercase = false;
            identity.Password.RequireUppercase = false;
            identity.Password.RequireNonAlphanumeric = false;
            identity.Password.RequiredUniqueChars = 1;
            identity.Lockout.AllowedForNewUsers = true;
            identity.Lockout.MaxFailedAccessAttempts = o.LockoutMaxFailedAttempts;
            identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(o.LockoutMinutes);
        });
        AddSessionRules(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AccountReader>();
        services.AddSingleton<BootstrapGuard>();
        services.AddScoped<SignInFailure>();
        services.AddSingleton<BearerTokens>();
        services.AddScoped<RefreshChains>();

        services.AddHealthChecks().AddNpgSql(
            sp => sp.GetRequiredService<IOptions<IdentityModuleOptions>>().Value.ConnectionSettings().ConnectionString,
            name: "identity-postgres",
            tags: ["identity"]);
    }

    public Task InitializeAsync(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<IdentityModuleOptions>>().Value;
        if (string.IsNullOrEmpty(options.DataProtectionCertificatePath) && !serviceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            serviceProvider.GetRequiredService<ILogger<IdentityModule>>().LogWarning(
                "The Data Protection key ring is stored unencrypted in the identity database; anyone who can read it can forge " +
                "sessions. Set Identity:DataProtectionCertificatePath (and Password) to encrypt it.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Keys live in the identity database so replicas share them and restarts keep sessions. With a certificate
    /// configured, each key is encrypted with it; keys written before stay readable.
    /// </summary>
    private static void AddDataProtection(IServiceCollection services, IConfiguration configuration)
    {
        var dataProtection = services.AddDataProtection().SetApplicationName("skanyxx").PersistKeysToDbContext<AccountsDbContext>();
        var options = configuration.GetSection(IdentityModuleOptions.Section).Get<IdentityModuleOptions>();
        if (string.IsNullOrEmpty(options?.DataProtectionCertificatePath))
            return;

        var certificate = new X509Certificate2(options.DataProtectionCertificatePath, options.DataProtectionCertificatePassword);
        dataProtection.ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
    }

    /// <summary>
    /// The browser session: the cookie is re-checked against the security stamp every
    /// <see cref="IdentityModuleOptions.SecurityStampValidationSeconds"/> (so a sign-out elsewhere ends it), and dies
    /// <see cref="IdentityModuleOptions.SessionDays"/> after the password sign-in however actively it is used.
    /// </summary>
    private static void AddSessionRules(IServiceCollection services)
    {
        services.AddOptions<SecurityStampValidatorOptions>().Configure<IOptions<IdentityModuleOptions>>((stamp, module) =>
            stamp.ValidationInterval = TimeSpan.FromSeconds(module.Value.SecurityStampValidationSeconds));

        // Post-configure: runs after the Host's AddIdentityCookies, which installs the stamp validator this wraps.
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .PostConfigure<TimeProvider, IOptions<IdentityModuleOptions>>((cookie, time, module) =>
            {
                var validateStamp = cookie.Events.OnValidatePrincipal;
                cookie.Events.OnValidatePrincipal = async context =>
                {
                    if (SessionStart.Read(context.Properties) is not { } start
                        || time.GetUtcNow() >= start.AddDays(module.Value.SessionDays))
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                        return;
                    }

                    await validateStamp(context);
                };
            });
    }
}
