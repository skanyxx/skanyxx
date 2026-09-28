using MediatR;
using Microsoft.AspNetCore.Identity;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;

namespace Skanyxx.Module.Identity.Features.Me;

/// <summary>Read from the store rather than the token, so roles and name are current.</summary>
internal sealed class GetMeHandler(UserManager<IdentityUser> users, AccountReader accounts) : IRequestHandler<GetMeQuery, Outcome<AccountDto>>
{
    public async Task<Outcome<AccountDto>> Handle(GetMeQuery query, CancellationToken ct) =>
        await users.FindByIdAsync(query.UserId) is { } user
            ? Outcome<AccountDto>.Ok(await accounts.ToDtoAsync(user))
            : Outcome<AccountDto>.NotFound("This account no longer exists.");
}
