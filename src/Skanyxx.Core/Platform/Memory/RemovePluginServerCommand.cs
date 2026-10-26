using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// The server's record and stored values are deleted: it left main and kagent, or (with <paramref name="UnclaimedOnly"/>)
/// nothing will ever deploy it, so its values must not wait for a later install to inherit them. <paramref name="Plugin"/>
/// and <paramref name="UpdatedBefore"/>, when given, must still hold at the delete: a record a new install wrote meanwhile
/// is kept.
/// </summary>
/// <returns>True when a record was deleted.</returns>
public sealed record RemovePluginServerCommand(string Actor, string Server, bool UnclaimedOnly = false, string? Plugin = null, DateTime? UpdatedBefore = null)
    : IRequest<Outcome<bool>>;
