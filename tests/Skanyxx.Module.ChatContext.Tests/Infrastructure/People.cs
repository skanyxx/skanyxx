using System.Security.Claims;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;

namespace Skanyxx.Module.ChatContext.Tests.Infrastructure;

/// <summary>Signed-in people as the Host would hand them to a handler.</summary>
public static class People
{
    public const string Ana = "ana";
    public const string Bob = "bob";
    public const string Cleo = "cleo";
    public const string Boss = "boss";
    public const string Olivia = "olivia";

    public static ChatReader Person(string userId) => User(userId);

    public static ChatReader Supervisor => User(Boss, SkanyxxRoles.Supervisor);

    public static ChatReader Owner => User(Olivia, SkanyxxRoles.Owner);

    public static string[] Roles(string userId) => userId switch
    {
        Boss => [SkanyxxRoles.Supervisor],
        Olivia => [SkanyxxRoles.Owner],
        _ => []
    };

    private static ChatReader User(string userId, params string[] roles) =>
        ChatReader.From(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId), .. roles.Select(r => new Claim(ClaimTypes.Role, r))], "Test")));
}
