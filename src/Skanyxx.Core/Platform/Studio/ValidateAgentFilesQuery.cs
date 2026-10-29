using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>
/// The agent repo's CI asks the studio to judge one agent folder with the rules a merge applies (D362): the YAML scan,
/// the strict shape, the form's values, secrets in text and the cloud tools licence. Read-only; authorized by the CI
/// token, not a person.
/// </summary>
public sealed record ValidateAgentFilesQuery(string Name, string AgentYaml, string GrantsYaml) : IRequest<Outcome<AgentFilesCheck>>;
