using System.Text;
using MediatR;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Core.Platform.Plugins;

/// <summary>
/// The owner enters header values for a plugin server in main (only the header names its plugin declares). Stored
/// encrypted, never in git, never logged or returned; the reconciler hands them to kagent as the server's Secret.
/// </summary>
public sealed record SetPluginServerSecretsCommand(StudioUser User, string Server, IReadOnlyDictionary<string, string> Values)
    : IRequest<Outcome<StudioResult>>
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"User = {User?.UserId}, Server = {Server}, Values = [{string.Join(", ", (Values?.Keys ?? []).Select(k => $"{k} = ***"))}]");
        return true;
    }
}
