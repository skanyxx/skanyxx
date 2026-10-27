using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.ChatContext.Domain;

namespace Skanyxx.Module.ChatContext.Access;

/// <summary>
/// Who may read and share a chat session (D237), on memory's rules (memory-hierarchy.md, AccessPolicy) with one
/// difference: a personal session is its owner's alone, with no supervisor or owner exception. A shared session is read
/// like a card in its scope: company by everyone signed in, team/department by members and by supervisors (oversight).
/// Sharing writes like an upsert: personal (own), a team/department one is a member of, company only for a supervisor.
/// Membership is asked live (no caching across requests).
/// </summary>
internal sealed class SessionAccess(IOrgMembership org)
{
    public async Task<Readable> ReadableAsync(ChatReader user, CancellationToken ct)
    {
        var me = user.UserId;
        // No personal scope here: one's own sessions are matched by owner, and nobody else's personal scope is ever readable.
        List<string> scopes = [SessionScope.Company];
        if (user.IsSupervisor)
            return new Readable(me, scopes, AllOrg: true);

        var membership = await org.ForUserAsync(me, ct);
        scopes.AddRange(membership.Teams.Select(t => $"team:{t}"));
        scopes.AddRange(membership.Departments.Select(d => $"department:{d}"));
        return new Readable(me, scopes, AllOrg: false);
    }

    public static bool CanRead(Readable readable, ChatSession session) =>
        session.UserId == readable.UserId
        || readable.Scopes.Contains(session.Scope)
        || (readable.AllOrg && SessionScope.IsOrg(session.Scope));

    public async Task<bool> CanShareToAsync(ChatReader user, string scope, CancellationToken ct)
    {
        var me = user.UserId;
        if (scope == SessionScope.Personal(me))
            return true;
        if (scope == SessionScope.Company)
            return user.IsSupervisor;
        if (!SessionScope.IsOrg(scope))
            return false;

        var membership = await org.ForUserAsync(me, ct);
        var (level, id) = (scope[..scope.IndexOf(':')], scope[(scope.IndexOf(':') + 1)..]);
        return level == "team" ? membership.Teams.Contains(id) : membership.Departments.Contains(id);
    }
}
