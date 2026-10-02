using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
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
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Entra;
using Skanyxx.Module.Identity.Org;

namespace Skanyxx.Module.Identity;

/// <summary>
/// Local accounts (ASP.NET Core Identity on Postgres): owner bootstrap, password sign-in (cookie or bearer), refresh,
/// sign-out, break-glass unlock, invites, role assignment, the org tree (departments, teams, members; D055), which
/// other modules read through <see cref="IOrgMembership"/>, and the optional Microsoft Entra ID sign-in that maps
/// groups onto roles and teams (D027). The Host owns the cookie and bearer schemes and the fallback policy; this module
/// owns the store, the session rules (D1, D025, D026) and its own external scheme, <c>entra</c>.
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
            .Validate<IHostEnvironment>((o, environment) => o.HasValidPublicBaseUrl(environment.IsDevelopment()),
                "Identity:PublicBaseUrl must be empty or an absolute https:// URL written exactly (no spaces, backslashes, query, " +
                "fragment or user info), e.g. https://skanyxx.example.com; http only for a loopback host such as http://localhost:5282.")
            .ValidateOnStart();

        services.AddDbContext<AccountsDbContext>((sp, o) => o.UseNpgsql(
            sp.GetRequiredService<IOptions<IdentityModuleOptions>>().Value.ConnectionSettings().ConnectionString,
            npgsql => npgsql.MigrationsHistoryTable(AccountsDbContext.MigrationsTable)));
        services.AddHostedService<IdentityMigrator>();
        AddDataProtection(services, configuration);

        services.AddIdentityCore<IdentityUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AccountsDbContext>()
            .AddPasswordValidator<EmailPasswordValidator>()
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
        services.AddScoped<SessionIssuer>();
        services.AddScoped<SessionRevocation>();
        services.AddScoped<PrivilegeRevocation>();
        services.AddScoped<InviteLinks>();
        services.AddScoped<ClientAddress>();
        services.AddHttpContextAccessor();
        services.AddSingleton<IAuthorizationHandler, OwnerRouteRefusals>();
        services.AddScoped<IOrgMembership, OrgMembershipReader>();
        AddEntraSignIn(services);

        services.AddHealthChecks().AddNpgSql(
            sp => sp.GetRequiredService<IOptions<IdentityModuleOptions>>().Value.ConnectionSettings().ConnectionString,
            name: "identity-postgres",
            tags: ["identity"]);
    }

    public Task InitializeAsync(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            return Task.CompletedTask;

        var options = serviceProvider.GetRequiredService<IOptions<IdentityModuleOptions>>().Value;
        var logger = serviceProvider.GetRequiredService<ILogger<IdentityModule>>();
        if (string.IsNullOrEmpty(options.DataProtectionCertificatePath))
            logger.LogWarning(
                "The Data Protection key ring is stored unencrypted in the identity database; anyone who can read it can forge " +
                "sessions. Set Identity:DataProtectionCertificatePath (and Password) to encrypt it.");
        if (options.PublicBaseUri is not { } publicBase)
            logger.LogWarning("Identity:PublicBaseUrl is not set; invites cannot be created until it is. {Example}", InviteLinks.Example);
        else if (publicBase.IsLoopback && ListensBeyondLoopback(serviceProvider.GetRequiredService<IConfiguration>()))
            logger.LogWarning(
                "Identity:PublicBaseUrl is {PublicBaseUrl}, a loopback address, but Skanyxx listens beyond this machine: an invite link " +
                "would send each invitee to their own machine. Set it to the address people use to reach Skanyxx.", publicBase);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether a configured listen address (<c>urls</c>, <c>http_ports</c>/<c>https_ports</c>, Kestrel endpoints) is
    /// reachable from other machines. Nothing configured is Kestrel's default, localhost.
    /// </summary>
    private static bool ListensBeyondLoopback(IConfiguration configuration)
    {
        if (!string.IsNullOrEmpty(configuration["http_ports"]) || !string.IsNullOrEmpty(configuration["https_ports"]))
            return true;
        var urls = (configuration["urls"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(configuration.GetSection("Kestrel:Endpoints").GetChildren().Select(e => e["Url"]).OfType<string>());
        return urls.Any(url => BindingAddress.Parse(url).Host is var host
            && host != "localhost" && !(IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip)));
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

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(options.DataProtectionCertificatePath, options.DataProtectionCertificatePassword);
        // Without the RSA private key, keys encrypted now cannot be decrypted after a restart: every session, token,
        // invite and the Entra secret would silently stop working.
        using (var rsa = certificate.GetRSAPrivateKey())
            if (rsa is null)
                throw new InvalidOperationException("Identity:DataProtectionCertificatePath must be a PFX with an RSA private key.");
        dataProtection.ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
    }

    /// <summary>
    /// The browser session: the cookie is checked against the security stamp on every request (one primary-key read,
    /// as bearer tokens are), so a sign-out elsewhere, a role change or disabling refuses it on the very next request,
    /// and it dies <see cref="IdentityModuleOptions.SessionDays"/> after the password sign-in however actively it is used.
    /// </summary>
    private static void AddSessionRules(IServiceCollection services)
    {
        // Replaces Identity's interval stamp validator (installed by the Host's AddIdentityCookies): a stale cookie
        // must not keep its old roles for any window. Post-configure: runs after the Host's cookie setup.
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .PostConfigure<TimeProvider, IOptions<IdentityModuleOptions>>((cookie, time, module) =>
                cookie.Events.OnValidatePrincipal = async context =>
                {
                    var signIn = context.HttpContext.RequestServices.GetRequiredService<SignInManager<IdentityUser>>();
                    if (SessionStart.Read(context.Properties) is { } start
                        && time.GetUtcNow() < start.AddDays(module.Value.SessionDays)
                        && await signIn.ValidateSecurityStampAsync(context.Principal) is not null)
                        return;

                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                });

        // Identity's bearer handler checks only a token's signature and expiry. This makes it check the stamp as well, on
        // every request (one primary-key read), so sign-out, a role change or disabling refuses access tokens at once
        // instead of letting them run out their hour. Post-configure: the Host's AddBearerToken registers the scheme.
        services.AddOptions<BearerTokenOptions>(IdentityConstants.BearerScheme).PostConfigure(bearer =>
        {
            var received = bearer.Events.OnMessageReceived;
            bearer.Events.OnMessageReceived = async context =>
            {
                await received(context);
                // The token the handler will authenticate: one set by an earlier event, else the header (same parsing).
                var header = context.Request.Headers.Authorization.ToString();
                var token = context.Token ?? (header.StartsWith(BearerPrefix, StringComparison.Ordinal) ? header[BearerPrefix.Length..] : null);
                // Anything the handler would refuse anyway (unreadable, expired) is left to it, without a database read.
                if (context.Result is not null || token is null
                    || context.Options.BearerTokenProtector.Unprotect(token) is not { } ticket
                    || ticket.Properties.ExpiresUtc is not { } expires || expires < (context.Options.TimeProvider ?? TimeProvider.System).GetUtcNow())
                    return;

                var signIn = context.HttpContext.RequestServices.GetRequiredService<SignInManager<IdentityUser>>();
                if (await signIn.ValidateSecurityStampAsync(ticket.Principal) is null)
                    context.Fail("The session has ended; sign in again.");
            };
        });
    }

    /// <summary>
    /// Microsoft Entra ID as an external login (D027): an OIDC scheme whose options are built from the settings the owner
    /// saves (<see cref="EntraOidcOptions"/>), so a change applies without a restart. The settings refresher is registered
    /// after <see cref="IdentityMigrator"/>, so it first reads them once the tables exist.
    /// </summary>
    private static void AddEntraSignIn(IServiceCollection services)
    {
        services.AddSingleton<EntraEndpoints>();
        services.AddSingleton<EntraRedirectUri>();
        services.AddSingleton<EntraKeyRing>();
        services.AddSingleton<EntraSettingsCache>();
        services.AddHostedService<EntraSettingsRefresher>();
        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, EntraOidcOptions>();
        services.AddAuthentication().AddOpenIdConnect(EntraScheme.Name, EntraScheme.DisplayName, _ => { });
        services.AddHttpClient<IGraphMembership, GraphMembership>(http => http.Timeout = TimeSpan.FromSeconds(10));
        // What the OIDC handler would give its own client: its timeout, a 10 MB cap and its user agent. The options cache
        // keeps this client until the next settings save, maybe for months, so its connections are recycled by lifetime
        // instead (CR m3): DNS changes at login.microsoftonline.com are picked up within five minutes.
        services.AddHttpClient(EntraOidcOptions.Backchannel, http =>
        {
            http.Timeout = TimeSpan.FromMinutes(1);
            http.MaxResponseContentBufferSize = 10 * 1024 * 1024;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Microsoft ASP.NET Core OpenIdConnect handler");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            // The options cache holds one client for months; the factory's rotation would only mark its handler expired (I5).
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        services.AddScoped<EntraMapper>();
        services.AddScoped<EntraAccounts>();
        services.AddScoped<EntraPasswordRule>();
        services.AddScoped<PasswordStepUp>();
        services.AddScoped<ExternalLogins>();
    }

    private const string BearerPrefix = "Bearer ";
}
