using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// SEC L1: the Data Protection key ring lives in the identity database, next to the protected client secret. Without
/// <c>Identity:DataProtectionCertificatePath</c> the keys are stored in clear, so a database copy yields the secret —
/// which (with <c>GroupMember.Read.All</c>) reads group membership across the tenant. Warned about, not refused
/// (<see cref="Core.Platform.Identity.EntraSettingsDto.SecretKeysWarning"/>).
/// </summary>
internal sealed class EntraKeyRing(IOptions<IdentityModuleOptions> options, IHostEnvironment environment)
{
    public bool Unencrypted => !environment.IsDevelopment() && string.IsNullOrEmpty(options.Value.DataProtectionCertificatePath);
}
