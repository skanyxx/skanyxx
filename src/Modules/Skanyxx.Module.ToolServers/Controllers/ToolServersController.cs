using Skanyxx.Core.Platform;
using Microsoft.AspNetCore.Authorization;
using Skanyxx.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.ToolServers.Controllers;

// D116: what changes kagent, the cluster or runs a process on the Host is the owner's alone, like D109.
[ApiController]
[Route("api/toolservers")]
public class ToolServersController : ControllerBase
{
    private readonly IToolServerService _toolServerService;
    private readonly KAgentApiClient _kagent;

    public ToolServersController(IToolServerService toolServerService, KAgentApiClient kagent)
    {
        _toolServerService = toolServerService;
        _kagent = kagent;
    }

    [HttpGet]
    public async Task<ActionResult<List<ToolServer>>> GetAll()
    {
        return await _toolServerService.GetAllAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ToolServer>> GetById(string id)
    {
        var server = await _toolServerService.GetByIdAsync(id);
        if (server == null) return NotFound();
        return server;
    }

    [Authorize(Roles = SkanyxxRoles.Owner)]
    [HttpPost]
    public async Task<ActionResult<ToolServer>> Create([FromBody] CreateToolServerRequest request)
    {
        try
        {
            var server = await _kagent.CreateToolServerAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = server.Ref }, server);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = SkanyxxRoles.Owner)]
    [HttpDelete("{namespace}/{name}")]
    public async Task<ActionResult> Delete(string @namespace, string name)
    {
        try
        {
            await _kagent.DeleteToolServerAsync(@namespace, name);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("calls/recent")]
    public async Task<ActionResult<List<ToolCall>>> GetRecentCalls([FromQuery] int limit = 10)
    {
        return await _toolServerService.GetRecentCallsAsync(limit);
    }

    [HttpGet("metrics")]
    public async Task<ActionResult> GetMetrics()
    {
        var servers = await _toolServerService.GetAllAsync();
        return Ok(new
        {
            TotalServers = servers.Count,
            AvailableTools = servers.Sum(s => s.ToolCount),
            CallsToday = 0,
            AvgLatencyMs = 0
        });
    }

    [Authorize(Roles = SkanyxxRoles.Owner)]
    [HttpPost("{id}/restart")]
    public async Task<ActionResult> Restart(string id)
    {
        try
        {
            await _toolServerService.RestartAsync(id);
            return Ok(new { message = "Server restarted" });
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    [Authorize(Roles = SkanyxxRoles.Owner)]
    [HttpPost("{id}/ping")]
    public async Task<ActionResult> Ping(string id)
    {
        try
        {
            var success = await _toolServerService.PingAsync(id);
            return Ok(new { status = success ? "Online" : "Offline" });
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    [Authorize(Roles = SkanyxxRoles.Owner)]
    [HttpPost("{serverId}/tools/{toolName}/invoke")]
    public async Task<ActionResult> InvokeTool(string serverId, string toolName, [FromBody] object parameters)
    {
        try
        {
            var result = await _toolServerService.InvokeToolAsync(serverId, toolName, parameters);
            return Ok(result);
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }
}
