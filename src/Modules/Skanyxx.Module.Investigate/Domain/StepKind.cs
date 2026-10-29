using System.Text.Json.Serialization;

namespace Skanyxx.Module.Investigate.Domain;

/// <summary>An agent step asks one kagent agent; a group holds steps.</summary>
[JsonConverter(typeof(LowerCaseEnum<StepKind>))]
public enum StepKind
{
    Agent,
    Group
}
