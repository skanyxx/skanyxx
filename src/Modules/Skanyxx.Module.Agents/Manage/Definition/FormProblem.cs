namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>One broken rule of the agent form, keyed by the form field it belongs to (the page shows it under that field).</summary>
internal sealed record FormProblem(string Field, string Message);
