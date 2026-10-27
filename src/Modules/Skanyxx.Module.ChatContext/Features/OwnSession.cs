using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.ChatContext.Access;
using Skanyxx.Module.ChatContext.Data;
using Skanyxx.Module.ChatContext.Domain;

namespace Skanyxx.Module.ChatContext.Features;

/// <summary>
/// The session for a change only its owner may make: not found when the person may not read it (so its existence is not
/// revealed), forbidden when they may read it but do not own it.
/// </summary>
internal static class OwnSession
{
    public static async Task<(ChatSession? Session, Outcome<T>? Refusal)> LoadAsync<T>(
        ChatContextDbContext db, SessionAccess access, ChatReader user, string sessionId, CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([sessionId], ct);
        if (session is null || !SessionAccess.CanRead(await access.ReadableAsync(user, ct), session))
            return (null, Outcome<T>.NotFound("No such session."));
        return session.UserId == user.UserId
            ? (session, null)
            : (null, Outcome<T>.Forbidden("Only the person who had this chat may change it."));
    }
}
