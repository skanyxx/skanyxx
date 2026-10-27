using Skanyxx.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Models;

namespace Skanyxx.Module.Agents.Controllers;

/// <summary>Read-only list of kagent's agents. Agents are created and changed through the studio (D110).</summary>
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
}
