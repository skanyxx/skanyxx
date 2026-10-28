using System.Security.Claims;
using System.Text.Json.Nodes;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Core.Services;
using Skanyxx.Module.ToolServers.Data;
using Skanyxx.Module.ToolServers.Definition;
using Skanyxx.Module.ToolServers.Setup;

namespace Skanyxx.Module.ToolServers.Features;

/// <summary>
/// The Tool servers page over kagent's controller API (kagent 0.10.2: list, create, delete; no read and no update of one
/// server, so an edit deletes and re-creates it). Header values pass through once, to kagent, and are never stored,
/// logged or returned. Roles are the endpoints' (owner writes, supervisor reads and refreshes).
/// </summary>
internal sealed class ToolServerCatalog(
    KAgentApiClient kagent, ToolServersDbContext db, IMediator mediator, IConfiguration configuration, TimeProvider clock, ILogger<ToolServerCatalog> logger)
{
    /// <summary>The studio's memory servers (StudioNames.MemoryServerPrefix in the agents module).</summary>
    private const string MemoryServerPrefix = "skanyxx-memory-";

    public async Task<ToolServersOverview> OverviewAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var snapshot = await SnapshotAsync(ct);
        var owner = Caller.IsOwner(user);
        return new ToolServersOverview(snapshot.KAgent, snapshot.Views(owner), owner, ToolServerProtocols.All, SupportedKind.All);
    }

    /// <summary>kagent rediscovers every server's tools each minute on its own; this reads what it found now.</summary>
    public async Task<Outcome<ToolServerView>> RefreshAsync(ClaimsPrincipal user, string ns, string name, CancellationToken ct)
    {
        var snapshot = await SnapshotAsync(ct);
        if (!snapshot.KAgent.Reachable)
            return Outcome<ToolServerView>.Unavailable(ToolServerFailure.KAgent);
        return snapshot.View($"{ns}/{name}", Caller.IsOwner(user)) is { } view
            ? Outcome<ToolServerView>.Ok(view)
            : Outcome<ToolServerView>.NotFound(ToolServerFailure.NotFound);
    }

    public async Task<Outcome<ToolServerSaved>> CreateAsync(ClaimsPrincipal user, ToolServerForm form, CancellationToken ct)
    {
        Normalize(form);
        Validate(form);
        var (ns, name) = (form.Namespace!, form.Name!);
        if (await db.Servers.FindAsync([ns, name], ct) is not null)
            return Outcome<ToolServerSaved>.Conflict(null, ToolServerFailure.Exists($"{ns}/{name}"), "exists");
        if ((await StudioServersAsync(ct))($"{ns}/{name}"))
            return Outcome<ToolServerSaved>.Conflict(null, ToolServerFailure.Studio, "studio");
        var userId = Caller.UserId(user);
        var (record, secrets) = Build(form, userId);
        try
        {
            if ((await ListAsync(ct)).ContainsKey(record.Ref))
                return Outcome<ToolServerSaved>.Conflict(null, ToolServerFailure.Exists(record.Ref), "exists");
            await ToolServerAudit.WriteAsync(mediator, ToolServerAudit.CreateRequested, userId, ns, name, record);
            // From the intent row on, the person leaving does not cancel the write half way.
            await kagent.CreateRemoteMcpServerAsync(KAgentServerObject.For(record), secrets, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Tool server {Namespace}/{Name}: kagent did not create it", ns, name);
            await ToolServerAudit.OutcomeAsync(mediator, logger, ToolServerAudit.Failed, userId, ns, name, record, "create");
            return Outcome<ToolServerSaved>.Unavailable(ToolServerFailure.Create);
        }
        db.Servers.Add(record);
        await db.SaveChangesAsync(CancellationToken.None);
        await ToolServerAudit.OutcomeAsync(mediator, logger, ToolServerAudit.Created, userId, ns, name, record);
        logger.LogWarning("Tool server {Namespace}/{Name} created by {ActorUserId}", ns, name, userId);
        return Outcome<ToolServerSaved>.Created(new ToolServerSaved(record.Ref));
    }

    /// <summary>Only a server added on this page: kagent has no update, and the full spec of any other is unknown here.</summary>
    public async Task<Outcome<ToolServerSaved>> UpdateAsync(ClaimsPrincipal user, ToolServerForm form, CancellationToken ct)
    {
        Normalize(form);
        Validate(form);
        var (ns, name) = (form.Namespace!, form.Name!);
        var existing = await db.Servers.FindAsync([ns, name], ct);
        if (existing is null)
            return await RefusalAsync(ns, name, ToolServerFailure.NotCreatedHere, ct);
        var userId = Caller.UserId(user);
        var (record, secrets) = Build(form, userId);
        var removed = false;
        try
        {
            await ToolServerAudit.WriteAsync(mediator, ToolServerAudit.UpdateRequested, userId, ns, name, record);
            await kagent.DeleteRemoteMcpServerAsync(ns, name, CancellationToken.None);
            removed = true;
            await kagent.CreateRemoteMcpServerAsync(KAgentServerObject.For(record), secrets, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Tool server {Namespace}/{Name}: kagent did not {Step} it", ns, name, removed ? "re-create" : "delete");
            await ToolServerAudit.OutcomeAsync(mediator, logger, ToolServerAudit.Failed, userId, ns, name, record, removed ? "update: re-create" : "update: delete");
            if (!removed)
                return Outcome<ToolServerSaved>.Unavailable(ToolServerFailure.KAgent);
            // The old server is gone: the record says what the page tried, so the page shows "not registered" and its fix.
            Copy(record, existing);
            await db.SaveChangesAsync(CancellationToken.None);
            return Outcome<ToolServerSaved>.Unavailable(ToolServerFailure.Recreate);
        }
        Copy(record, existing);
        await db.SaveChangesAsync(CancellationToken.None);
        await ToolServerAudit.OutcomeAsync(mediator, logger, ToolServerAudit.Updated, userId, ns, name, record);
        logger.LogWarning("Tool server {Namespace}/{Name} updated by {ActorUserId}", ns, name, userId);
        return Outcome<ToolServerSaved>.Ok(new ToolServerSaved(record.Ref));
    }

    /// <summary>A server agents use is deleted only when <paramref name="confirmInUse"/> (409 <c>in-use</c> otherwise, with the agents).</summary>
    public async Task<Outcome<ToolServerDeleted>> DeleteAsync(ClaimsPrincipal user, string ns, string name, bool confirmInUse, CancellationToken ct)
    {
        var snapshot = await SnapshotAsync(ct);
        if (!snapshot.KAgent.Reachable)
            return Outcome<ToolServerDeleted>.Unavailable(ToolServerFailure.KAgent);
        var reference = $"{ns}/{name}";
        var view = snapshot.View(reference, owner: true);
        if (view is null)
            return Outcome<ToolServerDeleted>.NotFound(ToolServerFailure.NotFound);
        if (view.Source == ServerSources.Studio)
            return Outcome<ToolServerDeleted>.Conflict(null, ToolServerFailure.Studio, "studio");
        if (!view.Supported)
            return Outcome<ToolServerDeleted>.Conflict(null, ToolServerFailure.NotManaged(view.Kind), view.Kind == ToolServerKinds.Stdio ? "stdio" : "not-managed");
        if (!confirmInUse && view.UsedBy is not { Count: 0 })
            return Outcome<ToolServerDeleted>.Conflict(new ToolServerDeleted(reference, view.UsedBy ?? []),
                view.UsedBy is null ? ToolServerFailure.UsageUnknown : ToolServerFailure.InUse, view.UsedBy is null ? "usage-unknown" : "in-use");

        var userId = Caller.UserId(user);
        var record = snapshot.Records.GetValueOrDefault(reference);
        try
        {
            await ToolServerAudit.WriteAsync(mediator, ToolServerAudit.DeleteRequested, userId, ns, name, record);
            await kagent.DeleteRemoteMcpServerAsync(ns, name, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Tool server {Namespace}/{Name}: kagent did not delete it", ns, name);
            await ToolServerAudit.OutcomeAsync(mediator, logger, ToolServerAudit.Failed, userId, ns, name, record, "delete");
            return Outcome<ToolServerDeleted>.Unavailable(ToolServerFailure.KAgent);
        }
        await db.Servers.Where(s => s.Namespace == ns && s.Name == name).ExecuteDeleteAsync(CancellationToken.None);
        await ToolServerAudit.OutcomeAsync(mediator, logger, ToolServerAudit.Deleted, userId, ns, name, record);
        logger.LogWarning("Tool server {Namespace}/{Name} deleted by {ActorUserId} (used by {AgentCount} agents)", ns, name, userId, view.UsedBy?.Count);
        return Outcome<ToolServerDeleted>.Ok(new ToolServerDeleted(reference, view.UsedBy ?? []));
    }

    private async Task<Outcome<ToolServerSaved>> RefusalAsync(string ns, string name, string notCreatedHere, CancellationToken ct)
    {
        var snapshot = await SnapshotAsync(ct);
        var reference = $"{ns}/{name}";
        if (snapshot.IsStudio(reference))
            return Outcome<ToolServerSaved>.Conflict(null, ToolServerFailure.Studio, "studio");
        return snapshot.Entry(reference) is { } entry
            ? Outcome<ToolServerSaved>.Conflict(null, entry.Kind == ToolServerKinds.Remote ? notCreatedHere : ToolServerFailure.NotManaged(entry.Kind), "not-created-here")
            : Outcome<ToolServerSaved>.NotFound(ToolServerFailure.NotFound);
    }

    private async Task<ToolServerSnapshot> SnapshotAsync(CancellationToken ct)
    {
        var records = await db.Servers.AsNoTracking().ToDictionaryAsync(s => s.Namespace + "/" + s.Name, ct);
        var studio = await StudioServersAsync(ct);
        var address = KAgentAddress();
        try
        {
            var listed = await ListAsync(ct);
            return new ToolServerSnapshot(new KAgentStatus(true, address, null), listed, await AgentsAsync(ct), records, studio);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Tool servers: kagent did not list them");
            var facts = new FixFacts(address) { KAgentStatus = (int?)(ex as HttpRequestException)?.StatusCode };
            var fix = facts.KAgentStatus is not null ? ToolServerFixes.KAgentRefused(facts)
                : ToolServerFormRules.IsLoopback(new Uri(address)) ? ToolServerFixes.KAgentPortForward(facts)
                : ToolServerFixes.KAgentUnreachable(facts);
            return new ToolServerSnapshot(new KAgentStatus(false, address, fix), null, null, records, studio);
        }
    }

    /// <summary>The first entry of a repeated ref wins (m14); an entry without a <c>ns/name</c> ref is skipped.</summary>
    private async Task<Dictionary<string, KAgentEntry>> ListAsync(CancellationToken ct) =>
        (await kagent.GetToolServerListAsync(ct))
            .Select(s => new KAgentEntry(Text(s?["ref"]) ?? "", Text(s?["groupKind"]) ?? "",
                [.. (s?["discoveredTools"] as JsonArray ?? []).Select(t => new ToolView(Text(t?["name"]) ?? "", Text(t?["description"]) ?? ""))
                    .Where(t => t.Name.Length > 0).OrderBy(t => t.Name, StringComparer.Ordinal)]))
            .Where(e => e.Ref.Contains('/'))
            .DistinctBy(e => e.Ref)
            .ToDictionary(e => e.Ref);

    private async Task<IReadOnlyList<Agent>?> AgentsAsync(CancellationToken ct)
    {
        try
        {
            return await kagent.GetAgentsAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Tool servers: kagent did not list agents, so which agents use a server is unknown");
            return null;
        }
    }

    /// <summary>The studio's servers: every agent's memory server and each plugin server, in Studio:Namespace.</summary>
    private async Task<Func<string, bool>> StudioServersAsync(CancellationToken ct)
    {
        var ns = configuration["Studio:Namespace"] ?? ToolServerFormRules.DefaultNamespace;
        var plugins = (await mediator.Send(new PluginServerRecordsQuery(), ct)).Value ?? [];
        var names = plugins.Select(p => $"{ns}/{p.Server}").ToHashSet();
        return reference => names.Contains(reference) || reference.StartsWith($"{ns}/{MemoryServerPrefix}", StringComparison.Ordinal);
    }

    private (ToolServerRecord Record, IReadOnlyList<(string Name, string Key, string Value)> Secrets) Build(ToolServerForm form, string? userId)
    {
        var companion = KAgentServerObject.CompanionSecret(form.Name!);
        var record = new ToolServerRecord
        {
            Namespace = form.Namespace!,
            Name = form.Name!,
            Url = form.Url!,
            Protocol = form.Protocol!,
            TimeoutSeconds = form.TimeoutSeconds,
            Description = form.Description!,
            Headers = [.. form.Headers.Select(h => h.Source == HeaderSources.Value
                ? new ToolServerHeader(h.Name!, companion, h.Name!, true)
                : new ToolServerHeader(h.Name!, h.SecretName!, h.SecretKey!, false))],
            UpdatedBy = userId,
            UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        return (record, [.. form.Headers.Where(h => h.Source == HeaderSources.Value).Select(h => (companion, h.Name!, h.Value!))]);
    }

    private static void Copy(ToolServerRecord from, ToolServerRecord to)
    {
        to.Url = from.Url;
        to.Protocol = from.Protocol;
        to.TimeoutSeconds = from.TimeoutSeconds;
        to.Description = from.Description;
        to.Headers = from.Headers;
        to.UpdatedBy = from.UpdatedBy;
        to.UpdatedAt = from.UpdatedAt;
    }

    private static void Normalize(ToolServerForm form)
    {
        form.Namespace = string.IsNullOrWhiteSpace(form.Namespace) ? ToolServerFormRules.DefaultNamespace : form.Namespace.Trim();
        form.Name = form.Name?.Trim();
        form.Url = form.Url?.Trim();
        form.Protocol ??= ToolServerProtocols.StreamableHttp;
        form.Description = form.Description?.Trim() ?? "";
        form.Headers ??= [];
    }

    private static void Validate(ToolServerForm form)
    {
        if (ToolServerFormRules.Problems(form) is { Count: > 0 } problems)
            throw ToolServerFailure.Invalid(problems);
        form.Url = new Uri(form.Url!).AbsoluteUri;
    }

    private string KAgentAddress()
    {
        var config = new KAgentConfig();
        configuration.GetSection("KAgent").Bind(config);
        return Origin(new Uri(config.GetFullUrl()));
    }

    /// <summary>Scheme, host and port only: never a user name or password, a path or a query.</summary>
    internal static string Origin(Uri uri) => uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Port, UriFormat.UriEscaped);

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
