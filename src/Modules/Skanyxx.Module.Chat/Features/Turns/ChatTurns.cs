using System.Text;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.MediatR.Requests;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Core.Services;
using Skanyxx.Module.Chat.Features.Agents;
using Skanyxx.Module.Chat.Features.Conversations;
using Skanyxx.Module.Chat.Features.Files;

namespace Skanyxx.Module.Chat.Features.Turns;

/// <summary>
/// What every turn does around kagent, blocking or streamed. Only a merged agent (by namespace and name), and only the
/// caller's own conversation with that agent: kagent looks sessions up by id and user, so another person's conversation
/// id is "not found", and a conversation with a different agent is refused the same way. A first turn that fails, or
/// that the caller abandons, deletes the session it just created, so retries leave no empty sessions behind.
/// Memory and the chat index take part through Core interfaces (no-ops without their modules). A new conversation's first
/// message may be answered from the answer cache; otherwise it carries related prior chats (chat index) and the related
/// cards both the person and the agent may read (memory). Every answered turn updates the index and the session's card.
/// </summary>
internal sealed class ChatTurns(
    KAgentApiClient kagent, IRelatedMemory related, ICardExtractor extractor, IChatContext context, IChatQuota quota, ChatFiles files,
    ILogger<ChatTurns> logger)
{
    public const int MaxTitle = 80;

    /// <summary>kagent failing throws (<see cref="HttpRequestException"/>, <see cref="TimeoutException"/>).</summary>
    public async Task<Outcome<TurnStart>> StartAsync(SendChatMessageCommand command, CancellationToken ct)
    {
        if (await MergedAgents.FindAsync(kagent, command.AgentNamespace, command.AgentName, ct) is not { } agent)
            return Outcome<TurnStart>.NotFound("No such agent to chat with.");
        var agentRef = $"{agent.Namespace}/{agent.Name}";
        if (await quota.RefusalAsync(command.UserId, agentRef, ct) is { } refusal)
            return Outcome<TurnStart>.RateLimited(refusal);
        var attached = await FilesAsync(command.UserId, command.FileIds ?? [], agent, ct);
        if (attached.Value is not { } turnFiles)
            return attached.Map<TurnStart>(_ => null!);

        var tickets = command.TicketKeys ?? [];
        var isNew = string.IsNullOrEmpty(command.ConversationId);
        // A question asked with files is never answered from the cache: the cache knows the question only.
        var prepared = isNew && command.Reader is { } reader
            ? await context.PrepareAsync(new ChatTurnStart(reader, agentRef, agent.Revision, command.Message, tickets,
                command.NoCache || turnFiles.Stored.Count > 0), ct)
            : ChatPreparation.None;
        if (prepared.Cached is { } cached)
            return Outcome<TurnStart>.Ok(new TurnStart(agent, agentRef, cached.ConversationId, false, Title(command, turnFiles), prepared,
                MemoryContext.None, command.Message, []));

        // An agent's memory id is its kagent name (the studio grants by it).
        var memory = isNew
            ? await related.ForNewChatAsync(new RelatedMemoryQuery(command.UserId, agent.Name, command.Message, tickets), ct)
            : MemoryContext.None;
        string sessionId, title;
        if (isNew)
        {
            title = Title(command, turnFiles);
            sessionId = (await kagent.CreateSessionAsync(agentRef, command.UserId, ct, title)).Id;
        }
        else if (await OwnConversation.FindAsync(kagent, command.UserId, command.ConversationId!, ct) is { } session
                 && session.AgentId == MergedAgents.SessionAgentId(agent))
            (sessionId, title) = (session.Id, OwnConversation.Title(session));
        else
            return Outcome<TurnStart>.NotFound(OwnConversation.NotFound);

        // The session is the last thing created: nothing below may leave a new one behind.
        try
        {
            foreach (var file in turnFiles.Stored)
                await files.AttachAsync(command.UserId, file, sessionId, ct);
        }
        catch when (isNew)
        {
            await DeleteSessionAsync(command.UserId, sessionId);
            await files.DetachConversationAsync(command.UserId, sessionId, CancellationToken.None);
            throw;
        }
        return Outcome<TurnStart>.Ok(new TurnStart(agent, agentRef, sessionId, isNew, title, prepared, memory,
            prepared.Context + memory.Block + turnFiles.Block + command.Message, turnFiles.Images));
    }

    /// <summary>Not the request's token: the answer exists, so the index, the card and the quota record it even if the caller left.</summary>
    public async Task CompleteAsync(SendChatMessageCommand command, TurnStart start, string answer, ChatUsage usage)
    {
        if (command.Reader is { } who)
            await context.CompletedAsync(new ChatTurnDone(
                who, start.SessionId, start.AgentRef, command.Message, answer, start.IsNew, command.TicketKeys ?? [], start.Prepared.CacheKey),
                CancellationToken.None);
        await extractor.ChatTurnAsync(new ChatTurnDigest(
            command.UserId, start.Agent.Name, start.AgentRef, start.SessionId, command.Message, answer, start.IsNew), CancellationToken.None);
        await RecordUsageAsync(command, start, usage);
    }

    /// <summary>A failed or abandoned first turn leaves no conversation behind. Not the request's token: the caller may be gone.</summary>
    public async Task AbandonAsync(SendChatMessageCommand command, TurnStart start)
    {
        if (!start.IsNew)
            return;
        await DeleteSessionAsync(command.UserId, start.SessionId);
        await files.DetachConversationAsync(command.UserId, start.SessionId, CancellationToken.None);
    }

    /// <summary>What a turn used, also when it failed or was stopped: the tokens were spent.</summary>
    public Task RecordUsageAsync(SendChatMessageCommand command, TurnStart start, ChatUsage usage) =>
        quota.RecordAsync(command.UserId, start.AgentRef, start.SessionId, usage, CancellationToken.None);

    private async Task DeleteSessionAsync(string userId, string sessionId)
    {
        try
        {
            await kagent.DeleteSessionAsync(sessionId, userId, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Could not delete the empty kagent session {SessionId}", sessionId);
        }
    }

    private static string Title(SendChatMessageCommand command, TurnFiles turnFiles)
    {
        var line = command.Message.Split('\n', 2)[0].Trim();
        line = line.Length > 0 ? line : turnFiles.Stored.Select(f => f.Name).FirstOrDefault() ?? OwnConversation.Untitled;
        return line.Length <= MaxTitle ? line : line[..MaxTitle];
    }

    private async Task<Outcome<TurnFiles>> FilesAsync(string userId, IReadOnlyList<string> ids, Agent agent, CancellationToken ct)
    {
        if (ids.Count == 0)
            return Outcome<TurnFiles>.Ok(TurnFiles.None);
        var block = new StringBuilder();
        var images = new List<A2AFilePart>();
        var stored = new List<StoredChatFile>();
        var budget = files.Options.MaxTextChars;
        foreach (var id in ids.Distinct())
        {
            if (await files.FindAsync(userId, id, ct) is not { } file)
                return Outcome<TurnFiles>.NotFound("A file you attached is no longer there. Attach it again.");
            var bytes = await files.ReadAsync(userId, file, ct);
            if (file.Kind == ChatFileKinds.Image)
            {
                if (!MergedAgents.AcceptsImages(agent))
                    return Outcome<TurnFiles>.Conflict(null,
                        $"{agent.Name} does not read images, so {file.Name} was not sent. Remove it and describe the image in words, " +
                        "or pick an agent that reads images.", "images_not_supported");
                images.Add(new A2AFilePart(file.Name, file.MediaType, bytes));
                block.Append(PromptBlocks.File(file.Name, file.Id, "(an image, sent with this message)"));
            }
            else
            {
                // requirements.md §1.4: what the person pasted into a file is redacted like everything else that reaches a prompt.
                var text = SecretText.Redact(FileContent.Text(bytes)!);
                if (text.Length > budget)
                    text = text[..budget] + $"\n(cut here: Chat sends at most {files.Options.MaxTextChars} characters of attached text)";
                budget = Math.Max(0, budget - text.Length);
                block.Append(PromptBlocks.File(file.Name, file.Id, text));
            }
            stored.Add(file);
        }
        return Outcome<TurnFiles>.Ok(new TurnFiles(block.ToString(), images, stored));
    }
}
