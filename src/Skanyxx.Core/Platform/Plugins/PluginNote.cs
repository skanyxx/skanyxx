namespace Skanyxx.Core.Platform.Plugins;

/// <summary>
/// One line of a plugin's install report. <see cref="Item"/> names the agent, server, tool or file; <see cref="Status"/>
/// is <c>changed</c> (renamed, cut, ignored field), <c>unsupported</c> (left out, the rest installs) or <c>refused</c>
/// (left out because it breaks a rule).
/// </summary>
public sealed record PluginNote(string Item, string Status, string Reason);
