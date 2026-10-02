using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>SEC S5: an optional certificate encrypts the key ring at rest; without one, a non-Development start warns.</summary>
[Collection(PostgresCollection.Name)]
public sealed class DataProtectionTests(PostgresFixture postgres) : IDisposable
{
    private const string Password = "pfx-password";
    private readonly string _pfx = Path.Combine(Path.GetTempPath(), $"skanyxx-dp-{Guid.NewGuid():N}.pfx");

    [Fact]
    public async Task WithACertificate_NewKeysAreEncrypted_AndSessionsStillWork()
    {
        WriteCertificate();
        var connectionString = await postgres.NewDatabaseAsync();
        await using var app = await IdentityApp.StartAsync(connectionString, s =>
        {
            s["Identity:DataProtectionCertificatePath"] = _pfx;
            s["Identity:DataProtectionCertificatePassword"] = Password;
        });
        await app.BootstrapAsync();

        var cookie = SetCookie.AuthHeader(await app.SignInAsync(useCookie: true));
        var me = await app.Client(cookie: cookie).GetAsync("/api/identity/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var keys = await scope.ServiceProvider.GetRequiredService<Data.AccountsDbContext>().DataProtectionKeys.Select(k => k.Xml).ToListAsync();
        Assert.NotEmpty(keys);
        Assert.All(keys, xml => Assert.Contains("<encryptedSecret", xml));
    }

    [Theory]
    [InlineData("Production", null, true)]
    [InlineData("Development", null, false)]
    [InlineData("Production", "certificate", false)]
    public async Task UnprotectedKeys_OutsideDevelopment_AreWarnedAbout(string environment, string? certificate, bool warned)
    {
        if (certificate is not null)
            WriteCertificate();
        var logs = new WarningLog();
        await using var app = IdentityApp.Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Identity"] = postgres.ConnectionString,
            ["Identity:DataProtectionCertificatePath"] = certificate is null ? null : _pfx,
            ["Identity:DataProtectionCertificatePassword"] = Password,
            ["Identity:PublicBaseUrl"] = IdentityApp.PublicBaseUrl
        }, environment, s => s.AddSingleton<ILoggerProvider>(logs));

        await new IdentityModule().InitializeAsync(app.Services);

        Assert.Equal(warned, logs.Warnings.Any(w => w.Contains("Identity:DataProtectionCertificatePath")));
    }

    // A PFX without the private key encrypts new keys and then cannot decrypt them after a restart: every session,
    // token, invite and the Entra secret would silently die, so startup refuses it.
    [Fact]
    public void ACertificateWithoutItsPrivateKey_StopsStartup()
    {
        WriteCertificate(withPrivateKey: false);

        var error = Assert.Throws<InvalidOperationException>(() => IdentityApp.Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Identity"] = postgres.ConnectionString,
            ["Identity:DataProtectionCertificatePath"] = _pfx,
            ["Identity:DataProtectionCertificatePassword"] = Password,
            ["Identity:PublicBaseUrl"] = IdentityApp.PublicBaseUrl
        }, "Production"));

        Assert.Contains("Identity:DataProtectionCertificatePath", error.Message);
    }

    public void Dispose() => File.Delete(_pfx);

    private void WriteCertificate(bool withPrivateKey = true)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=skanyxx-data-protection-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var exported = withPrivateKey ? certificate : X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        File.WriteAllBytes(_pfx, exported.Export(X509ContentType.Pfx, Password));
    }
}
