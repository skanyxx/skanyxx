using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Sessions.Controllers;

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

    [HttpGet]
    public async Task<ActionResult<List<KAgentSession>>> GetAll()
    {
        try { return await _kagent.GetSessionsAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get sessions"); return new List<KAgentSession>(); }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<KAgentSession>> GetById(string id)
    {
        try
        {
            var session = await _kagent.GetSessionAsync(id);
            if (session == null) return NotFound();
            return session;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get session {Id}", id); return NotFound(); }
    }

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

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        try { await _kagent.DeleteSessionAsync(id); return NoContent(); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to delete session {Id}", id); return NotFound(); }
    }

    [HttpGet("{id}/messages")]
    public async Task<ActionResult<List<ChatMessage>>> GetMessages(string id)
    {
        try { return await _kagent.GetSessionMessagesAsync(id); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get messages for session {Id}", id); return new List<ChatMessage>(); }
    }

    [HttpGet("{id}/events")]
    public async Task<ActionResult<List<KAgentEvent>>> GetEvents(string id, [FromQuery] int? limit = null)
    {
        try { return await _kagent.GetSessionEventsAsync(id, limit); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get events for session {Id}", id); return new List<KAgentEvent>(); }
    }
}

public class CreateSessionRequest
{
    public string AgentName { get; set; } = "";
}
