namespace Skanyxx.Module.Identity.Entra;

/// <summary>Microsoft Graph, for the group overage: which of the asked groups the user is in (transitively).</summary>
internal interface IGraphMembership
{
    /// <summary>The subset of <paramref name="groupIds"/> <paramref name="objectId"/> belongs to; empty when Graph does not know the user.</summary>
    Task<IReadOnlySet<string>> MemberOfAsync(EntraConfig config, Guid objectId, IReadOnlyCollection<string> groupIds, CancellationToken ct);
}
