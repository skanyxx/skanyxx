using Skanyxx.Core.Interfaces;
using Microsoft.AspNetCore.Http;
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

    [HttpGet("{namespace}/{name}")]
    public async Task<ActionResult<Agent>> Get(string @namespace, string name, CancellationToken ct)
    {
        try
        {
            var agent = await _agentService.GetAsync(@namespace, name, ct);
            if (agent == null) return NotFound();
            return agent;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "kagent did not answer. Try again in a moment." });
        }
    }
}
