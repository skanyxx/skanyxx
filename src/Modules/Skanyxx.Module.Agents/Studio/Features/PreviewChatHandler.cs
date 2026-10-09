using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Git;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>Talks only to the preview of an open proposal (by its labels), never to a merged or foreign agent.</summary>
internal sealed class PreviewChatHandler(
    IAgentRepo repo, KAgentApiClient kagent, StudioChat chat, IOptions<StudioOptions> options, ILogger<PreviewChatHandler> logger)
    : IRequestHandler<PreviewChatCommand, Outcome<ChatResponse>>
{
    public async Task<Outcome<ChatResponse>> Handle(PreviewChatCommand command, CancellationToken ct)
    {
        if (!command.User.CanEnter)
            return Outcome<ChatResponse>.Forbidden(StudioFailure.NotStudio);
        if (!options.Value.Configured)
            return StudioFailure.Conflict<ChatResponse>(StudioFailure.NotConfigured);
        var ns = options.Value.Namespace;
        try
        {
            if (await repo.PullAsync(command.Number, ct) is not { Open: true })
                return Outcome<ChatResponse>.NotFound(StudioFailure.NoProposal);
            var preview = (await kagent.GetAgentsAsync(ct)).FirstOrDefault(a => a.Namespace == ns
                && a.Labels.GetValueOrDefault(StudioNames.PreviewLabel) == command.Number.ToString()
                && a.Labels.GetValueOrDefault(StudioNames.ManagedByLabel) == StudioNames.ManagedBy
                && !a.Labels.ContainsKey(StudioNames.MergedLabel));
            if (preview is null)
                return Outcome<ChatResponse>.NotFound("This proposal has no preview yet: start one first.");
            return await chat.SendAsync(ns, preview.Name, command.User.UserId!, command.Message, command.ConversationId, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Studio preview chat on #{Number} failed", command.Number);
            return Outcome<ChatResponse>.Unavailable(StudioFailure.Unavailable);
        }
    }
}
