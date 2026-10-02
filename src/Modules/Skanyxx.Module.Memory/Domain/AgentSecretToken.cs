using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Skanyxx.Module.Memory.Domain;

/// <summary>
/// The agent secret's wire form: <c>skx_mem_</c> + 32 CSPRNG bytes as unpadded base64url (43 characters). The prefix
/// makes a leaked one easy to recognise (log and secret scanners). A plain SHA-256 is enough at rest: the input is
/// 256 random bits, not a password.
/// </summary>
public static class AgentSecretToken
{
    public const string Prefix = "skx_mem_";
    private const int SecretBytes = 32;
    private const int Length = 51; // Prefix + 43

    private static readonly SearchValues<char> Base64UrlAlphabet =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    public static string Generate() => Prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(SecretBytes));

    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    /// <summary>Rejects anything that cannot be a secret before it costs a database lookup.</summary>
    public static bool IsWellFormed(string secret) =>
        secret.Length == Length
        && secret.StartsWith(Prefix, StringComparison.Ordinal)
        && secret.AsSpan(Prefix.Length).IndexOfAnyExcept(Base64UrlAlphabet) < 0;
}
