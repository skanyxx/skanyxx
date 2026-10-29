using System.Text.Json.Serialization;

namespace Skanyxx.Module.Investigate.Domain;

/// <summary>How a group runs its steps: one after another, or all at once.</summary>
[JsonConverter(typeof(LowerCaseEnum<GroupMode>))]
public enum GroupMode
{
    Sequential,
    Parallel
}
