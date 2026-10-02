using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// Who may create or unlock the owner: with <c>Identity:BootstrapToken</c> set, whoever presents it (compared in
/// constant time). Without it, owner creation is allowed only in Development and only over a direct loopback
/// connection: elsewhere a sidecar, port-forward or header-less proxy also arrives from loopback.
/// </summary>
internal sealed class BootstrapGuard(IOptions<IdentityModuleOptions> options, IHostEnvironment environment)
{
    public bool HasToken => !string.IsNullOrEmpty(options.Value.BootstrapToken);

    public bool AllowsBootstrap(string? presentedToken, bool fromLoopback) =>
        HasToken ? Matches(presentedToken) : fromLoopback && environment.IsDevelopment();

    public bool Matches(string? presentedToken) =>
        // Hash first: FixedTimeEquals is only constant-time for equal lengths.
        HasToken && presentedToken is not null && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(presentedToken)), SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.BootstrapToken!)));
}
