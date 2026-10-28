using MediatR;

namespace Skanyxx.Core.Platform.Preferences;

/// <summary>Drops the caller's own choices: everything inherits the organization's defaults again.</summary>
public sealed record ResetPreferencesCommand(string UserId) : IRequest<Outcome<PreferencesView>>;
