using Skanyxx.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Models;

namespace Skanyxx.Module.Alerts.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlertsController : ControllerBase
{
    private readonly IAlertService _alertService;

    public AlertsController(IAlertService alertService)
    {
        _alertService = alertService;
    }

    [HttpGet]
    public async Task<ActionResult<List<Alert>>> GetAll([FromQuery] string? severity = null, [FromQuery] string? status = null)
    {
        return await _alertService.GetAllAsync(severity, status);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Alert>> GetById(string id)
    {
        var alert = await _alertService.GetByIdAsync(id);
        if (alert == null) return NotFound();
        return alert;
    }

    [HttpGet("stats")]
    public async Task<ActionResult> GetStats()
    {
        return Ok(await _alertService.GetStatsAsync());
    }

    [HttpPost("{id}/acknowledge")]
    public async Task<ActionResult> Acknowledge(string id)
    {
        try
        {
            var success = await _alertService.AcknowledgeAsync(id);
            if (!success) return NotFound();
            return Ok();
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    [HttpPost("{id}/resolve")]
    public async Task<ActionResult> Resolve(string id)
    {
        try
        {
            var success = await _alertService.ResolveAsync(id);
            if (!success) return NotFound();
            return Ok();
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<Alert>> Create([FromBody] Alert alert)
    {
        try
        {
            var created = await _alertService.CreateAsync(alert);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }
}
