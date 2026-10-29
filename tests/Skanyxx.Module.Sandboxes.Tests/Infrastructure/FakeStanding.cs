using System.Collections.Concurrent;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Sandboxes.Tests.Infrastructure;

/// <summary>Identity's live view of a person: everyone is an enabled supervisor unless a test says otherwise.</summary>
public sealed class FakeStanding : IPersonStanding
{
    /// <summary>A person's roles now; an empty array is a disabled account.</summary>
    public ConcurrentDictionary<string, string[]> Roles { get; } = new();

    public Task<IReadOnlySet<string>> ActiveRolesAsync(string userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlySet<string>>(Roles.GetValueOrDefault(userId, [SkanyxxRoles.Supervisor]).ToHashSet());
}
