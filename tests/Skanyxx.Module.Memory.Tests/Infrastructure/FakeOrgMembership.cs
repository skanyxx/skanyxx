using System.Collections.Concurrent;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Memory.Tests.Infrastructure;

/// <summary>
/// The org tree as memory sees it, in place of the identity module (memory must not reference it). Teams map to their
/// department; a person's departments are derived from their teams, as identity does. Changes count on the next call.
/// </summary>
public sealed class FakeOrgMembership : IOrgMembership
{
    private readonly ConcurrentDictionary<string, string> _departmentOf = new();
    private readonly ConcurrentDictionary<(string User, string Team), bool> _members = new();
    private int _lookups;

    /// <summary>How many times memory asked; a test resets it to count one request.</summary>
    public int Lookups { get => Volatile.Read(ref _lookups); set => Volatile.Write(ref _lookups, value); }

    /// <summary>Makes every lookup fail, as an unreachable identity database would.</summary>
    public bool Unavailable { get; set; }

    public void Team(string team, string department) => _departmentOf[team] = department;

    public void Join(string userId, string team, string department)
    {
        Team(team, department);
        _members[(userId, team)] = true;
    }

    public void Leave(string userId, string team) => _members.TryRemove((userId, team), out _);

    public Task<OrgMembership> ForUserAsync(string userId, CancellationToken ct)
    {
        Interlocked.Increment(ref _lookups);
        if (Unavailable)
            return Task.FromException<OrgMembership>(new InvalidOperationException("identity is unavailable"));
        var teams = _members.Keys.Where(k => k.User == userId).Select(k => k.Team).ToHashSet();
        return Task.FromResult(new OrgMembership(teams, teams.Select(t => _departmentOf[t]).ToHashSet()));
    }
}
