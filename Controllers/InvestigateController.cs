using Microsoft.AspNetCore.Mvc;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Controllers;

/// <summary>
/// Investigation controller - TODO: Implement investigation service
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class InvestigateController : ControllerBase
{
    private readonly ILogger<InvestigateController> _logger;

    public InvestigateController(ILogger<InvestigateController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<List<Investigation>> GetAll([FromQuery] string? status = null)
    {
        _logger.LogWarning("InvestigateController.GetAll not implemented - implement investigation service");
        return new List<Investigation>();
    }

    [HttpGet("{id}")]
    public ActionResult<Investigation> GetById(string id)
    {
        _logger.LogWarning("InvestigateController.GetById not implemented");
        return NotFound();
    }

    [HttpPost]
    public ActionResult<Investigation> Create([FromBody] InvestigateRequest request)
    {
        _logger.LogWarning("InvestigateController.Create not implemented - implement AI investigation");
        throw new NotImplementedException("Implement AI-powered investigation");
    }

    [HttpPost("{id}/resolve")]
    public ActionResult Resolve(string id)
    {
        throw new NotImplementedException("Implement investigation resolution");
    }
}
