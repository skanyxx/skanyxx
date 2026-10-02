using System.Text;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// The saved settings as this instance uses them, with the client secret in clear (memory only). <see cref="ClientSecret"/>
/// is null when none is set or the stored one cannot be decrypted (the key ring lost its key): sign-in is then off.
/// Printed with the secret as <c>***</c> (CR L3): this record travels into Graph calls and the mapper.
/// </summary>
internal sealed record EntraConfig(
    bool Enabled, string TenantId, string ClientId, string? ClientSecret, IReadOnlyDictionary<string, EntraGroupMapDto> Groups, int Version)
{
    public static readonly EntraConfig Disabled = new(false, "", "", null, new Dictionary<string, EntraGroupMapDto>(), 0);

    public bool CanSignIn => Enabled && ClientSecret is not null && Groups.Count > 0;

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Enabled = {Enabled}, TenantId = {TenantId}, ClientId = {ClientId}, ClientSecret = {(ClientSecret is null ? "null" : "***")}, " +
            $"Groups = {Groups.Count}, Version = {Version}");
        return true;
    }
}
