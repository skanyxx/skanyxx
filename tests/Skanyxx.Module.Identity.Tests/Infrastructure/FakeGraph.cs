using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Tests.Infrastructure;

/// <summary>Graph as the mapper sees it: answers from a fixed membership and records every question.</summary>
internal sealed class FakeGraph(params string[] memberOf) : IGraphMembership
{
    public List<(Guid ObjectId, IReadOnlyCollection<string> Asked)> Calls { get; } = [];

    public Exception? Fail { get; set; }

    public Task<IReadOnlySet<string>> MemberOfAsync(EntraConfig config, Guid objectId, IReadOnlyCollection<string> groupIds, CancellationToken ct)
    {
        Calls.Add((objectId, groupIds));
        return Fail is not null ? Task.FromException<IReadOnlySet<string>>(Fail) : Task.FromResult<IReadOnlySet<string>>(groupIds.Intersect(memberOf).ToHashSet());
    }
}
