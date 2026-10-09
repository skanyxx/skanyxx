using System.Security.Claims;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Tests.Infrastructure;

/// <summary>A <see cref="LibraryUser"/> the only way there is: from a signed-in principal, as the Host's sign-in builds it.</summary>
public static class People
{
    public static LibraryUser Person(string userId, params string[] roles) => LibraryUser.From(Principal(userId, TestAuthHandler.SchemeName, roles));

    public static ClaimsPrincipal Principal(string userId, string scheme, params string[] roles) =>
        new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, userId), .. roles.Select(r => new Claim(ClaimTypes.Role, r))], scheme));
}
