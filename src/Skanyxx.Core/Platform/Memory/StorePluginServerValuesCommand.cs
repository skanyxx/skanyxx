using System.Text;
using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// Stores header values for a plugin server, encrypted (Data Protection), merged into what is stored: a name given
/// replaces its value, the others stay. With <paramref name="KeepExisting"/> a name that already has a value keeps it (an
/// install must not change what a running server is sent before its merge). The values are bound to
/// <paramref name="Address"/> (the server URL's scheme, host, port and path): values stored for another one are dropped first,
/// never carried to a new address (D220). With <paramref name="KeepExisting"/>, values held for another address are not
/// touched at all (409 <c>moved</c>): a proposal that moves a server must not take the running one's values before its
/// merge. Refused when the record belongs to another plugin. Returns the fingerprint of the stored set (random, new when
/// the set changes).
/// </summary>
public sealed record StorePluginServerValuesCommand(string Actor, string Server, string Plugin, IReadOnlyDictionary<string, string> Values, bool KeepExisting, string Address)
    : IRequest<Outcome<string>>
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Actor = {Actor}, Server = {Server}, Plugin = {Plugin}, Values = [{string.Join(", ", (Values?.Keys ?? []).Select(k => $"{k} = ***"))}], "
            + $"KeepExisting = {KeepExisting}, Address = {Address}");
        return true;
    }
}
