namespace Skanyxx.Module.Tickets.Domain;

public sealed class Pipeline
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public List<PipelineStage> Stages { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
