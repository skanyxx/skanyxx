namespace Skanyxx.Module.Chat.Features.Files;

/// <param name="Warning">Plain words for the person when the file looks like it holds secrets (they are redacted before the agent sees it).</param>
internal sealed record ChatFileInfo(string Id, string Name, string Kind, long Size, bool SecretsFound, string? Warning)
{
    public const string SecretsWarning =
        "This file looks like it contains secrets (a password, key or token). Chat replaces them with [redacted] before the agent sees it; remove them from the file if you can.";

    public static ChatFileInfo From(StoredChatFile file) =>
        new(file.Id, file.Name, file.Kind, file.Size, file.SecretsFound, file.SecretsFound ? SecretsWarning : null);
}
