using Skanyxx.Core.Models;
using Skanyxx.Module.ToolServers.Data;
using Skanyxx.Module.ToolServers.Definition;
using Skanyxx.Module.ToolServers.Setup;

namespace Skanyxx.Module.ToolServers.Features;

/// <summary>
/// One read of everything the page shows: what kagent lists (null when it did not answer), the agents (null likewise),
/// Skanyxx's own records, and the studio's servers.
/// </summary>
internal sealed class ToolServerSnapshot(
    KAgentStatus kagent,
    IReadOnlyDictionary<string, KAgentEntry>? listed,
    IReadOnlyList<Agent>? agents,
    IReadOnlyDictionary<string, ToolServerRecord> records,
    Func<string, bool> isStudio)
{
    public KAgentStatus KAgent => kagent;

    public IReadOnlyDictionary<string, ToolServerRecord> Records => records;

    public KAgentEntry? Entry(string reference) => listed?.GetValueOrDefault(reference);

    public bool IsStudio(string reference) => isStudio(reference);

    /// <summary>The agents naming the server (<c>ns/name</c>), or null when kagent did not list agents.</summary>
    public IReadOnlyList<string>? UsedBy(string ns, string name) => agents?
        .Where(a => a.McpToolNames.Keys.Any(k => k == $"{ns}/{name}" || (k == name && a.Namespace == ns)))
        .Select(a => $"{a.Namespace}/{a.Name}")
        .Order(StringComparer.Ordinal)
        .ToList();

    public IReadOnlyList<ToolServerView> Views(bool owner) =>
        [.. (listed?.Keys ?? []).Union(records.Keys).Order(StringComparer.Ordinal).Select(r => View(r, owner)!)];

    /// <summary>Null when neither kagent nor Skanyxx knows the server.</summary>
    public ToolServerView? View(string reference, bool owner)
    {
        var record = records.GetValueOrDefault(reference);
        var entry = Entry(reference);
        if (record is null && entry is null)
            return null;
        var (ns, name) = Split(reference);
        var kind = entry?.Kind ?? ToolServerKinds.Remote;
        var source = record is not null ? ServerSources.Skanyxx : IsStudio(reference) ? ServerSources.Studio : ServerSources.Other;
        var usedBy = UsedBy(ns, name);
        var url = record is null ? null : new Uri(record.Url);
        var facts = new FixFacts(kagent.Address)
        {
            Namespace = ns,
            Name = name,
            Kind = kind,
            UrlOrigin = url is null ? null : ToolServerCatalog.Origin(url),
            LoopbackUrl = url is not null && ToolServerFormRules.IsLoopback(url),
            Protocol = record?.Protocol,
            SecretHeaders = record?.Headers.Where(h => !h.Companion).ToList() ?? [],
            ToolCount = entry?.Tools.Count,
            UsedBy = usedBy
        };
        var fixes = kagent.Reachable ? ServerDiagnosis.Of(facts, record, entry is not null) : [];
        var remote = kind == ToolServerKinds.Remote;
        return new ToolServerView(
            reference, ns, name, kind, ToolServerKinds.Label(kind), remote, source,
            // A path can carry a hosted server's key: the full URL is the owner's, a supervisor sees where it points.
            owner ? record?.Url : url is null ? null : ToolServerCatalog.Origin(url), record?.Protocol, record?.TimeoutSeconds, record?.Description,
            record?.Headers.Select(h => new HeaderView(h.Name, h.Companion ? HeaderSources.Value : HeaderSources.Secret, h.SecretName, h.SecretKey)).ToList() ?? [],
            entry?.Tools, usedBy,
            kagent.Reachable ? ServerDiagnosis.Status(fixes, entry?.Tools.Count) : ServerDiagnosis.Unknown,
            fixes,
            CanEdit: owner && remote && source == ServerSources.Skanyxx,
            CanDelete: owner && remote && source != ServerSources.Studio);
    }

    public static (string Namespace, string Name) Split(string reference)
    {
        var slash = reference.IndexOf('/');
        return (reference[..slash], reference[(slash + 1)..]);
    }
}
