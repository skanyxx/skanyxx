using System.Text.Json.Serialization;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>A CI check of a proposal's head commit (D362). None: no CI reported anything.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CheckState>))]
public enum CheckState
{
    None,
    Pending,
    Passed,
    Failed
}
