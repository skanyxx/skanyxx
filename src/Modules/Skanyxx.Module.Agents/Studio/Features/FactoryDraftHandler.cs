using System.Text.Json;
using System.Text.Json.Nodes;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// Factory as sugar over the form (D5, D022): the builder's words go to the factory agent (not merged, no tools), and
/// the fields of its JSON answer fill the form. Builders and the owner only; it creates nothing.
/// </summary>
internal sealed class FactoryDraftHandler(
    StudioReconciler reconciler, StudioChat chat, IOptions<StudioOptions> options, ILogger<FactoryDraftHandler> logger)
    : IRequestHandler<FactoryDraftCommand, Outcome<AgentDraft>>
{
    public async Task<Outcome<AgentDraft>> Handle(FactoryDraftCommand command, CancellationToken ct)
    {
        if (!command.User.CanPropose)
            return Outcome<AgentDraft>.Forbidden(StudioFailure.NotBuilder);
        if (!options.Value.Configured)
            return StudioFailure.Conflict<AgentDraft>(StudioFailure.NotConfigured);
        try
        {
            await reconciler.EnsureFactoryAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or StudioApplyException)
        {
            logger.LogWarning(ex, "Studio could not ensure the factory agent");
            return Outcome<AgentDraft>.Unavailable(StudioFailure.Unavailable);
        }

        var answer = await chat.SendAsync(options.Value.Namespace, StudioNames.FactoryAgent, command.User.UserId!, command.Request, null, ct);
        if (answer.Value is not { } response)
            return new Outcome<AgentDraft>(answer.Status, Message: answer.Message);
        return Parse(response.Message.Content, options.Value.FactoryModelConfig) is { } draft
            ? Outcome<AgentDraft>.Ok(draft)
            : Outcome<AgentDraft>.Unavailable("The factory did not answer with a draft. Try again, or describe the agent differently.");
    }

    /// <summary>The first JSON object in the answer (models sometimes wrap it in prose or a fence); fields it lacks stay empty.</summary>
    internal static AgentDraft? Parse(string answer, string modelConfig)
    {
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        try
        {
            if (JsonNode.Parse(answer[start..(end + 1)]) is not JsonObject json)
                return null;
            var search = json["search"] is JsonValue s && s.TryGetValue<bool>(out var sv) && sv;
            var upsert = json["upsert"] is JsonValue u && u.TryGetValue<bool>(out var uv) && uv;
            return new AgentDraft(Text(json["name"]), Text(json["description"]), modelConfig, Text(json["instructions"]), [], [],
                search || upsert ? [new StudioGrant("company", search, upsert)] : [], null).Normalized();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
}
