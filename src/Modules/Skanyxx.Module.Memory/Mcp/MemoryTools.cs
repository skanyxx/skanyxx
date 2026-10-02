using Skanyxx.Core.Platform;
using System.ComponentModel;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Endpoints;
using Skanyxx.Module.Memory.Features;
using Skanyxx.Module.Memory.Features.Cards;

namespace Skanyxx.Module.Memory.Mcp;

/// <summary>
/// The memory plane as kagent sees it. The agent is the owner of the secret the request authenticated with, the user
/// the X-User-Id header when that agent may act for users (D084) — never tool arguments, because the model fills those in.
/// </summary>
[McpServerToolType]
public sealed class MemoryTools(IMediator mediator, IHttpContextAccessor http)
{
    [McpServerTool(Name = "memory_search", ReadOnly = true)]
    [Description("Search company memory for locked decisions, facts, procedures and open questions. " +
                 "Returns a few short cards, best match first. Call this before answering questions about how the company works.")]
    public async Task<IReadOnlyList<CardHit>> SearchAsync(
        [Description("What you are looking for, in plain words.")] string query,
        CancellationToken ct)
    {
        return await Run(() => mediator.Send(new SearchCardsQuery(AgentCaller(), query), ct));
    }

    [McpServerTool(Name = "memory_upsert", Destructive = false, Idempotent = false)]
    [Description("Save ONE locked decision/fact/procedure/open question as a card. Do not save chat logs. " +
                 "Use version 0 to create; to change a card pass the version you last read. " +
                 "On a conflict, search again, then decide whether to update.")]
    public async Task<CardHit> UpsertAsync(
        [Description("Short slug, lowercase words joined by '-', e.g. 'refund-window'.")] string key,
        [Description("decision | fact | procedure | open")] string type,
        [Description("One line, at most 200 characters.")] string what,
        [Description("Why, at most 400 characters.")] string why,
        [Description("0 to create; otherwise the version you last read.")] int version = 0,
        [Description("'personal' (default, the user's own space) or a scope you were granted, e.g. 'company'.")] string scope = AgentGrant.CallerPersonal,
        CancellationToken ct = default)
    {
        var caller = AgentCaller();
        var target = scope == AgentGrant.CallerPersonal
            ? caller.PersonalScope?.ToString() ?? throw new McpException(
                "No user context: this call cannot write to 'personal' (the agent does not act for users, or X-User-Id is not a user id).")
            : scope;

        var outcome = await Run(() => mediator.Send(new UpsertCardCommand(caller, target, key, version, type, what, why), ct));
        return outcome.Status switch
        {
            OutcomeStatus.Ok or OutcomeStatus.Created => outcome.Value!.ToHit(),
            OutcomeStatus.Conflict => throw new McpException(
                $"Conflict: {outcome.Message} Search again for the current version, then decide."),
            _ => throw new McpException(outcome.Message!)
        };
    }

    private MemoryCaller AgentCaller() => MemoryHeaders.Agent(http.HttpContext!);

    private static async Task<T> Run<T>(Func<Task<T>> send)
    {
        try
        {
            return await send();
        }
        catch (ValidationException ex)
        {
            throw new McpException("Invalid arguments: " +
                string.Join("; ", ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")));
        }
    }
}
