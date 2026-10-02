using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// <c>skx_inv_</c> + 256 CSPRNG bits (base64url). Only the SHA-256 is stored and looked up: a copy of the database
/// cannot accept anyone's invite. A salt would add nothing against a 256-bit secret.
/// </summary>
internal static class InviteTokens
{
    private const string Prefix = "skx_inv_";

    public static string New() => Prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
