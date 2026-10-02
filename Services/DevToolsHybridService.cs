using Microsoft.EntityFrameworkCore;
using SkanyxxWeb.Data;
using SkanyxxWeb.Models;
using System.Diagnostics;

namespace SkanyxxWeb.Services;

public class DevToolsHybridService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly DevToolsLlmClient _llm;
    private readonly ILogger<DevToolsHybridService> _log;

    public DevToolsHybridService(
        IDbContextFactory<AppDbContext> dbFactory,
        DevToolsLlmClient llm,
        ILogger<DevToolsHybridService> log)
    {
        _dbFactory = dbFactory;
        _llm       = llm;
        _log       = log;
    }

    // ── Broadcast ─────────────────────────────────────────────────────────────

    /// <summary>Sends the same message to all requested LLM connections in parallel.</summary>
    public async Task<List<HybridLaneResult>> BroadcastAsync(HybridChatRequest req)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var connections = await db.DevToolsLlmConnections
            .Where(c => req.ConnectionIds.Contains(c.Id) && c.IsEnabled)
            .ToListAsync();

        var tasks = connections.Select(c => CallOneAsync(c, req.Message, req.Images, req.SystemPrompt));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    // ── Review ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Asks the reviewer LLM to critique another LLM's output and suggest improvements.
    /// </summary>
    public async Task<HybridReviewResult> ReviewAsync(HybridReviewRequest req)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var reviewer = await db.DevToolsLlmConnections.FindAsync(req.ReviewerConnectionId);
        if (reviewer is null) return new HybridReviewResult { Error = "Reviewer connection not found." };

        var prompt = $"""
            You are a rigorous code and solution reviewer.

            The original request was:
            {req.OriginalPrompt}

            The following response was produced by {req.AuthorLabel}:
            ---
            {req.ContentToReview}
            ---

            Review this response critically. Identify:
            1. Correctness issues or bugs
            2. Missing edge cases or error handling
            3. Style / quality problems
            4. What was done well

            End your review with a short "Suggested fix:" section that describes exactly what should be changed.
            """;

        var ws = ConnToWorkspace(reviewer);
        var messages = new List<DevToolsChatMessage>
            { new() { Role = "user", Content = prompt, Timestamp = DateTime.UtcNow } };

        try
        {
            var (reply, _, _) = await _llm.CallAsync(ws, "You are a precise, concise code reviewer.", messages);
            return new HybridReviewResult { Review = reply };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Hybrid review failed for connection {Id}", req.ReviewerConnectionId);
            return new HybridReviewResult { Error = ex.Message };
        }
    }

    // ── Fix ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends the review feedback back to the original author LLM so it can fix its own output.
    /// </summary>
    public async Task<HybridFixResult> FixAsync(HybridFixRequest req)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var author = await db.DevToolsLlmConnections.FindAsync(req.AuthorConnectionId);
        if (author is null) return new HybridFixResult { Error = "Author connection not found." };

        var ws = ConnToWorkspace(author);
        var messages = new List<DevToolsChatMessage>
        {
            new() { Role = "user",      Content = req.OriginalPrompt,  Timestamp = DateTime.UtcNow },
            new() { Role = "assistant", Content = req.OriginalReply,   Timestamp = DateTime.UtcNow },
            new() { Role = "user",      Content =
                $"A reviewer identified the following issues with your response:\n\n{req.ReviewFeedback}\n\n" +
                "Please fix your response, addressing all the issues mentioned above.",
                Timestamp = DateTime.UtcNow }
        };

        try
        {
            var (reply, _, _) = await _llm.CallAsync(ws, "You are an expert software engineer. Fix your previous response based on the review feedback.", messages);
            return new HybridFixResult { Reply = reply };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Hybrid fix failed for connection {Id}", req.AuthorConnectionId);
            return new HybridFixResult { Error = ex.Message };
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<HybridLaneResult> CallOneAsync(
        DevToolsLlmConnection conn, string message, List<string>? images, string? systemPrompt)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var ws = ConnToWorkspace(conn);
            var messages = new List<DevToolsChatMessage>
                { new() { Role = "user", Content = message, Images = images, Timestamp = DateTime.UtcNow } };
            var sys = string.IsNullOrEmpty(systemPrompt)
                ? "You are a helpful expert software engineering assistant."
                : systemPrompt;
            var (reply, _, _) = await _llm.CallAsync(ws, sys, messages);
            return new HybridLaneResult
            {
                ConnectionId = conn.Id, Name = conn.Name, Provider = conn.Provider,
                Model = conn.Model, Reply = reply, Ms = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            return new HybridLaneResult
            {
                ConnectionId = conn.Id, Name = conn.Name, Provider = conn.Provider,
                Model = conn.Model, Error = ex.Message, Ms = (int)sw.ElapsedMilliseconds
            };
        }
    }

    /// <summary>Creates a transient workspace from a connection record so the LLM client can call it.</summary>
    private static DevToolsWorkspace ConnToWorkspace(DevToolsLlmConnection c) => new()
    {
        LlmProvider = c.Provider,
        LlmModel    = c.Model,
        LlmUrl      = c.BaseUrl,
        ApiKey      = c.ApiKey
    };
}
