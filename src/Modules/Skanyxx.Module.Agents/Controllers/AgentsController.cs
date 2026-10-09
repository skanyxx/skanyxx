using Skanyxx.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Agents.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentsController : ControllerBase
{
    private readonly IAgentService _agentService;

    public AgentsController(IAgentService agentService)
    {
        _agentService = agentService;
    }

    [HttpGet]
    public async Task<ActionResult<List<Agent>>> GetAll()
    {
        return await _agentService.GetAllAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Agent>> GetById(string id)
    {
        var agent = await _agentService.GetByIdAsync(id);
        if (agent == null) return NotFound();
        return agent;
    }

    // D109: changing what kagent runs from here (kubectl apply/scale/delete) bypasses the studio's PR gate (D020, D024);
    // these stay as the owner's break-glass only. The studio is how agents are created and changed.
    [HttpPost]
    [Authorize(Roles = SkanyxxRoles.Owner)]
    public async Task<ActionResult<Agent>> Create([FromBody] CreateAgentRequest request)
    {
        var agent = await _agentService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = agent.Id }, agent);
    }

    [HttpPut("{id}/status")]
    [Authorize(Roles = SkanyxxRoles.Owner)]
    public async Task<ActionResult> UpdateStatus(string id, [FromBody] string status)
    {
        var success = await _agentService.UpdateStatusAsync(id, status);
        if (!success) return NotFound();
        return Ok();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = SkanyxxRoles.Owner)]
    public async Task<ActionResult> Delete(string id)
    {
        var success = await _agentService.DeleteAsync(id);
        if (!success) return NotFound();
        return NoContent();
    }
}
