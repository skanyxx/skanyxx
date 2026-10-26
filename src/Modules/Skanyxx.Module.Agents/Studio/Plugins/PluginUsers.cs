namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>A plugin server never leaves main while an agent outside the plugin still uses it (D219).</summary>
internal static class PluginUsers
{
    /// <param name="leaving">The plugin servers that would leave.</param>
    /// <param name="users">Server → the agents (outside the plugin) whose files name it.</param>
    /// <returns>Why they cannot leave yet, or null.</returns>
    public static string? Problem(IEnumerable<string> leaving, IReadOnlyDictionary<string, SortedSet<string>> users)
    {
        var used = leaving.Where(users.ContainsKey).Order(StringComparer.Ordinal).ToList();
        return used.Count == 0
            ? null
            : $"{string.Join("; ", used.Select(s => $"The server '{s}' is used by {string.Join(", ", users[s])}"))}: change or remove "
                + "those agents first, so none is left bound to a server that is gone.";
    }
}
