using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Features.Entra;

internal static class EntraSignIn
{
    /// <summary>The mapping, or null when Graph could not be asked (the overage path): nothing is changed then, the person retries.</summary>
    public static async Task<EntraMapping?> MapAsync(
        EntraMapper mapper, ExternalLoginInfo info, EntraConfig config, ILogger logger, ClientAddress client, CancellationToken ct)
    {
        try
        {
            return await mapper.MapAsync(info.Principal, config, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Microsoft sign-in {Key} from {RemoteIp}: Graph checkMemberGroups failed", info.ProviderKey, client.Current);
            return null;
        }
    }

    /// <summary>
    /// D9: a token without <c>acct</c> cannot tell a guest from a member, so it is refused — the app registration lacks
    /// the optional claim, which only the owner can add. Nothing about the account changes: the person is not at fault.
    /// </summary>
    public static Outcome<SignedIn>? RefuseUnknownKind(EntraMapping mapping, ExternalLoginInfo info, ILogger logger, ClientAddress client)
    {
        if (mapping.Kind != EntraAccountKind.Unknown)
            return null;
        logger.LogError("Microsoft sign-in {Key} from {RemoteIp} refused: the id token has no 'acct' claim. Add the optional claim 'acct' " +
            "to the ID token (app registration > Token configuration); until then nobody can sign in with Microsoft.", info.ProviderKey, client.Current);
        return Outcome<SignedIn>.Forbidden(EntraSignInMessages.NoAccountType);
    }
}
