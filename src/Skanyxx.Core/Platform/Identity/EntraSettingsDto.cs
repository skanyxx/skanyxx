namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// The Microsoft sign-in settings as the owner sees them. Never the client secret: only whether one is set.
/// <paramref name="Active"/>: sign-in is live on this instance (enabled and the stored secret is readable).
/// <paramref name="RedirectUri"/>: what to register in Entra; null in Development without <c>Identity:PublicBaseUrl</c>
/// (the request's own address is used then). <paramref name="SecretKeysUnencrypted"/>: a secret is stored while the
/// Data Protection keys that protect it are not encrypted (outside Development, no certificate configured).
/// </summary>
public sealed record EntraSettingsDto(
    bool Enabled, string TenantId, string ClientId, bool ClientSecretSet, bool Active, IReadOnlyList<EntraGroupMapDto> Groups,
    string? RedirectUri, string? UpdatedBy, DateTimeOffset? UpdatedAt, bool SecretKeysUnencrypted = false)
{
    /// <summary>What the page shows and the log says when <see cref="SecretKeysUnencrypted"/>: one string, so they cannot drift.</summary>
    public const string SecretKeysWarning =
        "The client secret is protected with Data Protection keys that are stored unencrypted in the same database: anyone with a copy " +
        "of it can read the secret. Set Identity:DataProtectionCertificatePath (and Password) to encrypt the keys.";
}
