using System.Collections.Concurrent;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.ChatContext.Tests.Infrastructure;

/// <summary>The org tree in place of the identity module: teams map to a department; nobody is in anything until joined.</summary>
public sealed class FakeOrgMembership : IOrgMembership
{
    private readonly ConcurrentDictionary<(string User, string Team), string> _members = new();

    public void Join(string userId, string team, string department) => _members[(userId, team)] = department;

    public Task<OrgMembership> ForUserAsync(string userId, CancellationToken ct)
    {
        var mine = _members.Where(m => m.Key.User == userId).ToList();
        return Task.FromResult(new OrgMembership(mine.Select(m => m.Key.Team).ToHashSet(), mine.Select(m => m.Value).ToHashSet()));
    }
}
