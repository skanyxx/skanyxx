using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Skanyxx.Module.Identity.Passwords;

/// <summary><c>skx_rst_</c> + 256 CSPRNG bits (base64url), like invite tokens; only the SHA-256 is stored and looked up.</summary>
internal static class ResetTokens
{
    private const string Prefix = "skx_rst_";

    public static string New() => Prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
