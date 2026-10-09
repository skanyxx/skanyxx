using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Models;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Investigate.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("api/investigations")]
public class InvestigateController : ControllerBase
{
    private readonly ILogger<InvestigateController> _logger;
    private static readonly List<Investigation> _investigations = new();
    private static readonly List<InvestigationTemplate> _templates = new()
    {
        new() { Id = "prod-incident", Name = "Production Incident", Description = "Immediate response for critical production issues", Urgency = "P0", Color = "red", Agents = new() { "k8s-agent", "observability-agent", "promql-agent" } },
        new() { Id = "perf-degradation", Name = "Performance Degradation", Description = "Analyze slow response times and resource usage", Urgency = "P1", Color = "orange", Agents = new() { "promql-agent", "observability-agent", "k8s-agent" } },
        new() { Id = "deployment-rollback", Name = "Deployment Rollback", Description = "Investigate failed deployments and rollback", Urgency = "P2", Color = "blue", Agents = new() { "k8s-agent", "argo-rollouts-agent", "helm-agent" } },
        new() { Id = "network-connectivity", Name = "Network Connectivity", Description = "Diagnose service mesh and network issues", Urgency = "P1", Color = "purple", Agents = new() { "cilium-debug-agent", "istio-agent", "kgateway-agent" } },
        new() { Id = "security-alert", Name = "Security Alert", Description = "Investigate security incidents and vulnerabilities", Urgency = "P0", Color = "red", Agents = new() { "k8s-agent", "cilium-debug-agent", "observability-agent" } },
        new() { Id = "capacity-planning", Name = "Capacity Planning", Description = "Resource utilization analysis and scaling", Urgency = "P3", Color = "green", Agents = new() { "promql-agent", "observability-agent", "k8s-agent" } }
    };

    public InvestigateController(ILogger<InvestigateController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<List<Investigation>> GetAll([FromQuery] string? status = null)
    {
        var result = status == null ? _investigations.OrderByDescending(i => i.CreatedAt).ToList() : _investigations.Where(i => i.Status == status).OrderByDescending(i => i.CreatedAt).ToList();
        return result;
    }

    [HttpGet("templates")]
    public ActionResult<List<InvestigationTemplate>> GetTemplates() => _templates;

    [HttpGet("{id}")]
    public ActionResult<Investigation> GetById(string id)
    {
        var investigation = _investigations.FirstOrDefault(i => i.Id == id);
        if (investigation == null) return NotFound(new { message = "Investigation not found" });
        return investigation;
    }

    [HttpPost]
    public ActionResult<InvestigationResult> Create([FromBody] InvestigateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query)) return BadRequest(new { message = "Query is required" });
        _logger.LogInformation("Starting investigation: {Query}", request.Query);

        // No agent call: talking to an agent is Chat's alone (D002, D017), as the signed-in person and only with merged
        // agents (D4). This leftover list is shared by every signed-in user, so it must never hold an agent's answer
        // (which could come from that person's personal memory, D084). It records the question and points to Chat.
        var investigation = new Investigation
        {
            Title = request.Query.Length > 50 ? request.Query[..50] + "..." : request.Query,
            Query = request.Query,
            Status = "completed",
            Summary = $"Investigation recorded for: {request.Query}",
            Findings = ["AI analysis runs in Chat with a company agent"],
            Recommendations = ["Ask the question in Chat"]
        };
        _investigations.Insert(0, investigation);
        if (_investigations.Count > 50) _investigations.RemoveAt(_investigations.Count - 1);

        return Ok(new InvestigationResult { Id = investigation.Id, Summary = investigation.Summary, Findings = investigation.Findings, Recommendations = investigation.Recommendations, Status = investigation.Status });
    }

    [HttpPost("template/{templateId}")]
    public ActionResult<InvestigationResult> CreateFromTemplate(string templateId, [FromBody] InvestigateRequest? request)
    {
        var template = _templates.FirstOrDefault(t => t.Id == templateId);
        if (template == null) return NotFound(new { message = "Template not found" });
        var query = request?.Query ?? $"{template.Name}: {template.Description}";
        var investigation = new Investigation { Title = template.Name, Query = query, TemplateId = templateId, Urgency = template.Urgency, Agents = new List<string>(template.Agents), Status = "in_progress" };
        investigation.Summary = $"Starting {template.Name} investigation...";
        investigation.Findings.Add($"Using template: {template.Description}");
        investigation.Findings.Add($"Recommended agents: {string.Join(", ", template.Agents)}");
        investigation.Recommendations.Add("Follow the standard runbook for this incident type");
        _investigations.Insert(0, investigation);
        return Ok(new InvestigationResult { Id = investigation.Id, Summary = investigation.Summary, Findings = investigation.Findings, Recommendations = investigation.Recommendations, Status = investigation.Status });
    }

    [HttpPost("{id}/resolve")]
    public ActionResult Resolve(string id, [FromBody] ResolveRequest? request)
    {
        var investigation = _investigations.FirstOrDefault(i => i.Id == id);
        if (investigation == null) return NotFound(new { message = "Investigation not found" });
        investigation.Status = "resolved";
        investigation.ResolvedAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(request?.Resolution)) investigation.Findings.Add($"Resolution: {request.Resolution}");
        _logger.LogInformation("Investigation {Id} resolved", id);
        return Ok(investigation);
    }

    [HttpDelete("{id}")]
    public ActionResult Delete(string id)
    {
        var investigation = _investigations.FirstOrDefault(i => i.Id == id);
        if (investigation == null) return NotFound(new { message = "Investigation not found" });
        _investigations.Remove(investigation);
        return NoContent();
    }
}

public class ResolveRequest
{
    public string? Resolution { get; set; }
}
