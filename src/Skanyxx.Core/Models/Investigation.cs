namespace Skanyxx.Core.Models;

public class Investigation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = "in_progress";
    public string Urgency { get; set; } = "P2";
    public string? TemplateId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public List<string> Findings { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public List<string> Agents { get; set; } = new();
    public Dictionary<string, object>? Metrics { get; set; }
}

public class InvestigationTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Urgency { get; set; } = "P2";
    public string Color { get; set; } = "blue";
    public List<string> Agents { get; set; } = new();
}

public class InvestigationResult
{
    public string Id { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Findings { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public Dictionary<string, object>? Metrics { get; set; }
    public string Status { get; set; } = "completed";
}
