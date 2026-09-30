using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Endpoints;
using Skanyxx.Module.Identity.Features.Refresh;
using Skanyxx.Module.Identity.Features.Unlock;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// SEC info: a record's generated ToString prints every member, so one logged as {Request}, interpolated or put in an
/// exception message would leak secrets. This covers ToString only: structured destructuring ({@Request}) reflects over
/// the properties and never calls it, and nothing logs these that way.
/// </summary>
public sealed class SecretRecordTests
{
    private const string Secret = "skx_inv_THE-SECRET-VALUE";
    private const string Email = "someone@skanyxx.example";

    public static TheoryData<object> Records => new()
    {
        new AcceptInviteCommand(Secret, Secret, "Bea", UseCookie: true),
        new CreateInviteCommand("actor", Email, ["builder"]),
        new InviteIssued("invite-1", "https://skanyxx.example/Invite?token=" + Secret, DateTimeOffset.UnixEpoch),
        new InviteStatusQuery(Secret),
        new SignInCommand(Email, Secret, UseCookie: false, Secret),
        new BootstrapOwnerCommand(Email, Secret, "Owner", Secret, FromLoopback: false),
        new RefreshCommand(Secret),
        new UnlockOwnerCommand(Email, Secret),
        new InviteCreatedResponse("invite-1", "https://skanyxx.example/Invite?token=" + Secret, DateTimeOffset.UnixEpoch)
    };

    [Theory]
    [MemberData(nameof(Records))]
    public void ToString_NeverPrintsTheSecret(object record)
    {
        var printed = record.ToString()!;

        Assert.DoesNotContain(Secret, printed);
        Assert.DoesNotContain(Email, printed);
        Assert.Contains("***", printed);
    }
}
