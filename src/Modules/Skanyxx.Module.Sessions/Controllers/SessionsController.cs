using Skanyxx.Core.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Sessions.Controllers;

// D116: what changes kagent, the cluster or runs a process on the Host is the owner's alone, like D109.
[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly KAgentApiClient _kagent;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(KAgentApiClient kagent, ILogger<SessionsController> logger)
    {
        _kagent = kagent;
        _logger = logger;
    }

    // D124: kagent keeps sessions per user, so a read is asked as a user and also checked against the session's user_id:
    // a signed-in person reads only their own sessions (Chat's and their studio previews'); the owner reads anyone's
    // by naming them (?user=), and without it the legacy console's own sessions.
    [HttpGet]
    public async Task<ActionResult<List<KAgentSession>>> GetAll([FromQuery] string? user = null)
    {
        var (asUser, allowed) = ReadAs(user);
        if (!allowed)
            return Forbid();
        try { return (await _kagent.GetSessionsAsync(asUser)).Where(s => asUser is null || s.UserId == asUser).ToList(); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get sessions"); return new List<KAgentSession>(); }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<KAgentSession>> GetById(string id, [FromQuery] string? user = null)
    {
        var (asUser, allowed) = ReadAs(user);
        if (!allowed)
            return Forbid();
        try
        {
            var session = await OwnSessionAsync(id, asUser);
            if (session == null) return NotFound();
            return session;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get session {Id}", id); return NotFound(); }
    }

    [Authorize(Roles = SkanyxxRoles.Owner)]
    [HttpPost]
    public async Task<ActionResult<KAgentSession>> Create([FromBody] CreateSessionRequest request)
    {
        try
        {
            var session = await _kagent.CreateSessionAsync(request.AgentName);
            return CreatedAtAction(nameof(GetById), new { id = session.Id }, session);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to create session for agent {Agent}", request.AgentName); return BadRequest(new { message = ex.Message }); }
    }

    [Authorize(Roles = SkanyxxRoles.Owner)]
    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        try { await _kagent.DeleteSessionAsync(id); return NoContent(); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to delete session {Id}", id); return NotFound(); }
    }

    [HttpGet("{id}/messages")]
    public async Task<ActionResult<List<ChatMessage>>> GetMessages(string id, [FromQuery] string? user = null)
    {
        var (asUser, allowed) = ReadAs(user);
        if (!allowed)
            return Forbid();
        try
        {
            if (await OwnSessionAsync(id, asUser) is null) return NotFound();
            return await _kagent.GetSessionMessagesAsync(id, asUser);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get messages for session {Id}", id); return new List<ChatMessage>(); }
    }

    [HttpGet("{id}/events")]
    public async Task<ActionResult<List<KAgentEvent>>> GetEvents(string id, [FromQuery] int? limit = null, [FromQuery] string? user = null)
    {
        var (asUser, allowed) = ReadAs(user);
        if (!allowed)
            return Forbid();
        try
        {
            if (await OwnSessionAsync(id, asUser) is null) return NotFound();
            return await _kagent.GetSessionEventsAsync(id, limit, asUser);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get events for session {Id}", id); return NotFound(); }
    }

    /// <summary>
    /// Whose sessions this caller reads: their own; for the owner, <paramref name="user"/> or (null) the console's own.
    /// Not allowed when someone else names another user.
    /// </summary>
    private (string? AsUser, bool Allowed) ReadAs(string? user)
    {
        if (Caller.IsOwner(User))
            return (string.IsNullOrEmpty(user) ? null : user, true);
        var me = Caller.UserId(User);
        return (me, me is not null && (string.IsNullOrEmpty(user) || user == me));
    }

    /// <summary>The session when kagent has it for <paramref name="asUser"/> and it says so itself; otherwise null.</summary>
    private async Task<KAgentSession?> OwnSessionAsync(string id, string? asUser) =>
        await _kagent.GetSessionAsync(id, asUser) is { } session && (asUser is null || session.UserId == asUser) ? session : null;
}

public class CreateSessionRequest
{
    public string AgentName { get; set; } = "";
}
