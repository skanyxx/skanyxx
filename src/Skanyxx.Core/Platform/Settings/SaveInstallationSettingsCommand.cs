using MediatR;

namespace Skanyxx.Core.Platform.Settings;

/// <summary>
/// Saves the given settings (owner only). Only values that differ from the effective one are written, each with its own
/// audit row; the result is the keys that changed.
/// </summary>
public sealed record SaveInstallationSettingsCommand(string ActorId, IReadOnlyDictionary<string, string> Values)
    : IRequest<Outcome<IReadOnlyList<string>>>;
