using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Accounts;

internal sealed class AccountReader(UserManager<IdentityUser> users)
{
    public async Task<AccountDto> ToDtoAsync(IdentityUser user)
    {
        var claims = await users.GetClaimsAsync(user);
        var roles = await users.GetRolesAsync(user);
        return new AccountDto(user.Id, user.Email!, claims.FirstOrDefault(c => c.Type == SkanyxxClaims.DisplayName)?.Value, [.. roles.Order()]);
    }

    public async Task<PersonDto> ToPersonAsync(IdentityUser user)
    {
        var account = await ToDtoAsync(user);
        return new PersonDto(account.Id, account.Email, account.DisplayName, account.Roles, AccountStatus.IsDisabled(user));
    }

    public static Claim DisplayNameClaim(string displayName) => new(SkanyxxClaims.DisplayName, displayName);
}
