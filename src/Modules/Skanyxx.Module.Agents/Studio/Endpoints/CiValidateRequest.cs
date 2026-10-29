namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>One agent folder as CI reads it from the checkout.</summary>
internal sealed class CiValidateRequest
{
    public string Name { get; set; } = "";
    public string AgentYaml { get; set; } = "";
    public string GrantsYaml { get; set; } = "";
}
