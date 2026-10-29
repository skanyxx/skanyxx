using System.Text.Json.Serialization;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>Where a merged agent is on its way to kagent (D363).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeployState>))]
public enum DeployState
{
    /// <summary>Not in kagent yet: the reconciler or Argo has not applied it.</summary>
    Waiting,

    /// <summary>Applied, not ready yet (Argo out of sync or progressing, kagent starting the pod).</summary>
    Syncing,

    /// <summary>Synced and healthy: it answers in Chat.</summary>
    Healthy,

    Failed,

    /// <summary>The owner stopped it (D121).</summary>
    Suspended,

    /// <summary>The deploy source could not be read.</summary>
    Unknown
}
