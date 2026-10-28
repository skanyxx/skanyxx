using MediatR;

namespace Skanyxx.Core.Platform.Preferences;

/// <summary>Replaces the caller's own choices with <paramref name="Preferences"/> (null fields inherit).</summary>
public sealed record SavePreferencesCommand(string UserId, PreferenceSet Preferences) : IRequest<Outcome<PreferencesView>>;
