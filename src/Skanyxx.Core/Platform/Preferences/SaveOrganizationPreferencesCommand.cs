using MediatR;

namespace Skanyxx.Core.Platform.Preferences;

/// <summary>Replaces the organization's defaults, which everyone inherits where they have not chosen. Owner only; audited.</summary>
public sealed record SaveOrganizationPreferencesCommand(string ActorId, PreferenceSet Preferences) : IRequest<Outcome<PreferenceSet>>;
