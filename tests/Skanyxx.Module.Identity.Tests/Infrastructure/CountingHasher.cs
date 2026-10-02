using Microsoft.AspNetCore.Identity;

namespace Skanyxx.Module.Identity.Tests.Infrastructure;

/// <summary>Counts password checks against the owner's hash; the decoy hash for unknown or locked accounts is not one.</summary>
public sealed class CountingHasher : IPasswordHasher<IdentityUser>
{
    private readonly PasswordHasher<IdentityUser> _inner = new();
    private int _ownerChecks;

    public int OwnerChecks
    {
        get => Volatile.Read(ref _ownerChecks);
        set => Volatile.Write(ref _ownerChecks, value);
    }

    public string HashPassword(IdentityUser user, string password) => _inner.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(IdentityUser user, string hashedPassword, string providedPassword)
    {
        if (user.Email == IdentityApp.OwnerEmail)
            Interlocked.Increment(ref _ownerChecks);
        return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}
