using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>
/// Plugin server header values at rest: ASP.NET Data Protection with its own purpose, on the key ring the identity module
/// keeps in its database (the Entra client secret's precedent, SEC L1: without
/// <c>Identity:DataProtectionCertificatePath</c> the keys are stored in clear).
/// </summary>
internal sealed class PluginServerValueProtector(IDataProtectionProvider protection)
{
    private const string Purpose = "Skanyxx.Memory.PluginServer.HeaderValues";

    private IDataProtector Protector => protection.CreateProtector(Purpose);

    public string Protect(IReadOnlyDictionary<string, string> values) => Protector.Protect(JsonSerializer.Serialize(values));

    /// <exception cref="System.Security.Cryptography.CryptographicException">The key that protected them is gone.</exception>
    public Dictionary<string, string> Unprotect(string protectedValues) =>
        new(JsonSerializer.Deserialize<Dictionary<string, string>>(Protector.Unprotect(protectedValues)) ?? [], StringComparer.OrdinalIgnoreCase);
}
