using Skanyxx.Core.Platform.Email;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Endpoints;
using Skanyxx.Module.Identity.Entra;
using Skanyxx.Module.Identity.Features.Refresh;
using Skanyxx.Module.Identity.Features.Unlock;
using Skanyxx.Module.Identity.Passwords;

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
        new InviteIssued("invite-1", "https://skanyxx.example/Invite?token=" + Secret, DateTimeOffset.UnixEpoch, Emailed: false),
        new InviteStatusQuery(Secret),
        new SignInCommand(Email, Secret, UseCookie: false, Secret),
        new BootstrapOwnerCommand(Email, Secret, "Owner", Secret, FromLoopback: false),
        new RefreshCommand(Secret),
        new UnlockOwnerCommand(Email, Secret),
        new InviteCreatedResponse("invite-1", "https://skanyxx.example/Invite?token=" + Secret, DateTimeOffset.UnixEpoch, Emailed: false),
        // D150/D156: the reset flow's records, and an email (its body carries the one-time link).
        new RequestPasswordResetCommand(Email),
        new PasswordResetStatusQuery(Secret),
        new CompletePasswordResetCommand(Secret, Secret),
        new PasswordResetIssued("https://skanyxx.example/ResetPassword?token=" + Secret, DateTimeOffset.UnixEpoch, Emailed: false),
        new EmailMessage(Email, "Reset your Skanyxx password", "Open https://skanyxx.example/ResetPassword?token=" + Secret),
        new ResetRequest(Email, "127.0.0.1", "https://skanyxx.example"),
        new SaveEntraSettingsCommand("actor", true, "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", Secret, []),
        new EntraChallengeQuery("/Account?handler=Linked", "user-1", Secret),
        new EntraConfig(true, "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", Secret,
            new Dictionary<string, EntraGroupMapDto>(), 1)
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
