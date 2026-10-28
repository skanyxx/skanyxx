using MediatR;

namespace Skanyxx.Core.Platform.Preferences;

/// <summary>The caller's own preferences: always <paramref name="UserId"/> from the principal, never from input.</summary>
public sealed record GetPreferencesQuery(string UserId) : IRequest<Outcome<PreferencesView>>;
