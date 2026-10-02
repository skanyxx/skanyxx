using System.Text.Json.Serialization;

namespace Skanyxx.Core.Models;

public class Hook
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = "kagent.dev/v1alpha1";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "EventHook";

    [JsonPropertyName("metadata")]
    public HookMetadata Metadata { get; set; } = new();

    [JsonPropertyName("spec")]
    public HookSpec Spec { get; set; } = new();

    [JsonPropertyName("status")]
    public HookStatus? Status { get; set; }
}

public class HookMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = "kagent";

    [JsonPropertyName("creationTimestamp")]
    public string? CreationTimestamp { get; set; }

    [JsonPropertyName("uid")]
    public string? Uid { get; set; }
}

public class HookSpec
{
    [JsonPropertyName("eventConfigurations")]
    public List<EventConfiguration> EventConfigurations { get; set; } = new();
}

public class EventConfiguration
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("agentRef")]
    public AgentReference AgentRef { get; set; } = new();

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;
}

public class AgentReference
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class HookStatus
{
    [JsonPropertyName("activeEvents")]
    public List<ActiveEventStatus>? ActiveEvents { get; set; }

    [JsonPropertyName("lastUpdated")]
    public string? LastUpdated { get; set; }
}

public class ActiveEventStatus
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("resourceName")]
    public string ResourceName { get; set; } = string.Empty;

    [JsonPropertyName("firstSeen")]
    public string FirstSeen { get; set; } = string.Empty;

    [JsonPropertyName("lastSeen")]
    public string LastSeen { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public class HookList
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "EventHookList";

    [JsonPropertyName("metadata")]
    public HookListMetadata Metadata { get; set; } = new();

    [JsonPropertyName("items")]
    public List<Hook> Items { get; set; } = new();
}

public class HookListMetadata
{
    [JsonPropertyName("resourceVersion")]
    public string ResourceVersion { get; set; } = string.Empty;
}
