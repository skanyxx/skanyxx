namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>
/// Something the Agents page needs that is missing, in plain words, with the numbered steps that fix it. Names of
/// settings only, never their values: the page copies this text for an agent to follow.
/// </summary>
/// <param name="Id">A stable token the page and tests key on (<c>kagent-unreachable</c>, <c>no-models</c>, …).</param>
internal sealed record SetupGuide(string Id, string Title, string Cause, IReadOnlyList<string> Steps);
