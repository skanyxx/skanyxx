using Microsoft.AspNetCore.Identity;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// One answer for every failed sign-in (unknown email, wrong password, locked out), and the same hashing cost for an
/// unknown email as for a real one, so neither the body nor the timing tells whether an account exists.
/// </summary>
internal sealed class SignInFailure(IPasswordHasher<IdentityUser> hasher)
{
    public const string Message = "Invalid email or password.";

    private static readonly IdentityUser Nobody = new();

    // Computed once per process with the configured hasher; a benign race at most hashes it twice.
    private static string? _decoyHash;

    public void SpendHashTime(string password) =>
        hasher.VerifyHashedPassword(Nobody, _decoyHash ??= hasher.HashPassword(Nobody, Guid.NewGuid().ToString()), password);
}
