namespace SkanyxxWeb.Models;

public class KAgentTask
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public TaskMetadata? Metadata { get; set; }
    public List<TaskHistoryItem>? History { get; set; }
}

public class TaskMetadata
{
    public KAgentUsageMetadata? KagentUsageMetadata { get; set; }
    public string? KagentType { get; set; }
}

public class KAgentUsageMetadata
{
    public int TotalTokenCount { get; set; }
}

public class TaskHistoryItem
{
    public string Kind { get; set; } = string.Empty;
    public List<TaskHistoryPart>? Parts { get; set; }
}

public class TaskHistoryPart
{
    public string Kind { get; set; } = string.Empty;
    public string? Text { get; set; }
    public TaskPartData? Data { get; set; }
    public TaskMetadata? Metadata { get; set; }
}

public class TaskPartData
{
    public string? Name { get; set; }
}
