using Microsoft.AspNetCore.Mvc;
using SkanyxxWeb.Models;
using SkanyxxWeb.Services;
using SkanyxxWeb.Interfaces;

namespace SkanyxxWeb.Controllers;

/// <summary>
/// Investigation controller - AI-powered infrastructure investigations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/investigations")] // Alias route
public class InvestigateController : ControllerBase
{
    private readonly ILogger<InvestigateController> _logger;
    private readonly IChatService _chatService;
    private readonly IAgentService _agentService;
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

    public InvestigateController(ILogger<InvestigateController> logger, IChatService chatService, IAgentService agentService)
    {
        _logger = logger;
        _chatService = chatService;
        _agentService = agentService;
    }

    [HttpGet]
    public ActionResult<List<Investigation>> GetAll([FromQuery] string? status = null)
    {
        var result = status == null
            ? _investigations.OrderByDescending(i => i.CreatedAt).ToList()
            : _investigations.Where(i => i.Status == status).OrderByDescending(i => i.CreatedAt).ToList();

        return result;
    }

    [HttpGet("templates")]
    public ActionResult<List<InvestigationTemplate>> GetTemplates()
    {
        return _templates;
    }

    [HttpGet("{id}")]
    public ActionResult<Investigation> GetById(string id)
    {
        var investigation = _investigations.FirstOrDefault(i => i.Id == id);
        if (investigation == null)
            return NotFound(new { message = "Investigation not found" });

        return investigation;
    }

    [HttpPost]
    public async Task<ActionResult<InvestigationResult>> Create([FromBody] InvestigateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { message = "Query is required" });

        _logger.LogInformation("Starting investigation: {Query}", request.Query);

        try
        {
            // Create the investigation record
            var investigation = new Investigation
            {
                Title = request.Query.Length > 50 ? request.Query[..50] + "..." : request.Query,
                Query = request.Query,
                Status = "in_progress"
            };

            // Try to get the first available agent
            var agents = await _agentService.GetAllAsync();
            var agent = agents.FirstOrDefault(a => a.Status == "Active") ?? agents.FirstOrDefault();

            if (agent != null)
            {
                investigation.Agents.Add(agent.Name);

                // Create an AI-powered investigation prompt
                var investigationPrompt = $@"You are an SRE assistant investigating an infrastructure issue. Analyze the following query and provide:
1. A brief summary of the issue
2. Key findings or observations
3. Recommended actions to resolve or investigate further

Issue/Query: {request.Query}

Please structure your response clearly with Summary, Findings, and Recommendations sections.";

                try
                {
                    // Send to KAgent for analysis
                    var chatRequest = new ChatRequest
                    {
                        Message = investigationPrompt,
                        AgentName = agent.Name
                    };

                    var response = await _chatService.SendMessageAsync(chatRequest);
                    var aiResponse = response.Message?.Content ?? "";

                    // Parse the AI response into structured data
                    investigation.Summary = ExtractSection(aiResponse, "Summary") ?? "Investigation completed";
                    investigation.Findings = ExtractListSection(aiResponse, "Findings");
                    investigation.Recommendations = ExtractListSection(aiResponse, "Recommendations");

                    if (investigation.Findings.Count == 0 && !string.IsNullOrEmpty(aiResponse))
                    {
                        // If parsing failed, use the raw response
                        investigation.Summary = aiResponse.Length > 200 ? aiResponse[..200] + "..." : aiResponse;
                        investigation.Findings.Add("AI analysis completed. See summary for details.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get AI analysis");
                    investigation.Summary = $"Investigation started for: {request.Query}";
                    investigation.Findings.Add("AI analysis unavailable - KAgent may be offline");
                    investigation.Findings.Add("Manual investigation recommended");
                    investigation.Recommendations.Add("Check KAgent connectivity");
                    investigation.Recommendations.Add("Review system logs manually");
                }
            }
            else
            {
                investigation.Summary = $"Investigation started for: {request.Query}";
                investigation.Findings.Add("No agents available for automated analysis");
                investigation.Recommendations.Add("Configure at least one KAgent to enable AI-powered investigation");
            }

            investigation.Status = "completed";
            _investigations.Insert(0, investigation);

            // Keep only last 50 investigations
            if (_investigations.Count > 50)
                _investigations.RemoveAt(_investigations.Count - 1);

            return Ok(new InvestigationResult
            {
                Id = investigation.Id,
                Summary = investigation.Summary,
                Findings = investigation.Findings,
                Recommendations = investigation.Recommendations,
                Status = investigation.Status
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Investigation failed");
            return StatusCode(500, new { message = $"Investigation failed: {ex.Message}" });
        }
    }

    [HttpPost("template/{templateId}")]
    public async Task<ActionResult<InvestigationResult>> CreateFromTemplate(string templateId, [FromBody] InvestigateRequest? request)
    {
        var template = _templates.FirstOrDefault(t => t.Id == templateId);
        if (template == null)
            return NotFound(new { message = "Template not found" });

        var query = request?.Query ?? $"{template.Name}: {template.Description}";

        var investigation = new Investigation
        {
            Title = template.Name,
            Query = query,
            TemplateId = templateId,
            Urgency = template.Urgency,
            Agents = new List<string>(template.Agents),
            Status = "in_progress"
        };

        // Use the template's recommended agents for analysis
        investigation.Summary = $"Starting {template.Name} investigation...";
        investigation.Findings.Add($"Using template: {template.Description}");
        investigation.Findings.Add($"Recommended agents: {string.Join(", ", template.Agents)}");
        investigation.Recommendations.Add("Follow the standard runbook for this incident type");

        investigation.Status = "in_progress";
        _investigations.Insert(0, investigation);

        return Ok(new InvestigationResult
        {
            Id = investigation.Id,
            Summary = investigation.Summary,
            Findings = investigation.Findings,
            Recommendations = investigation.Recommendations,
            Status = investigation.Status
        });
    }

    [HttpPost("{id}/resolve")]
    public ActionResult Resolve(string id, [FromBody] ResolveRequest? request)
    {
        var investigation = _investigations.FirstOrDefault(i => i.Id == id);
        if (investigation == null)
            return NotFound(new { message = "Investigation not found" });

        investigation.Status = "resolved";
        investigation.ResolvedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(request?.Resolution))
        {
            investigation.Findings.Add($"Resolution: {request.Resolution}");
        }

        _logger.LogInformation("Investigation {Id} resolved", id);
        return Ok(investigation);
    }

    [HttpDelete("{id}")]
    public ActionResult Delete(string id)
    {
        var investigation = _investigations.FirstOrDefault(i => i.Id == id);
        if (investigation == null)
            return NotFound(new { message = "Investigation not found" });

        _investigations.Remove(investigation);
        return NoContent();
    }

    private static string? ExtractSection(string text, string sectionName)
    {
        var patterns = new[] { $"{sectionName}:", $"**{sectionName}**:", $"## {sectionName}" };
        foreach (var pattern in patterns)
        {
            var idx = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var start = idx + pattern.Length;
                var end = text.IndexOf('\n', start + 1);
                if (end == -1) end = text.Length;

                // Look for the next section
                foreach (var nextPattern in new[] { "Findings:", "Recommendations:", "**", "##" })
                {
                    var nextIdx = text.IndexOf(nextPattern, start, StringComparison.OrdinalIgnoreCase);
                    if (nextIdx > start && nextIdx < end)
                        end = nextIdx;
                }

                return text[start..end].Trim();
            }
        }
        return null;
    }

    private static List<string> ExtractListSection(string text, string sectionName)
    {
        var result = new List<string>();
        var section = ExtractSection(text, sectionName);
        if (string.IsNullOrEmpty(section)) return result;

        var lines = section.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim().TrimStart('-', '*', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ' ');
            if (!string.IsNullOrWhiteSpace(trimmed))
                result.Add(trimmed);
        }

        return result;
    }
}

public class ResolveRequest
{
    public string? Resolution { get; set; }
}
