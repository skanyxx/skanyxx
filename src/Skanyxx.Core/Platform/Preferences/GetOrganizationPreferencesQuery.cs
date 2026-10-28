using MediatR;

namespace Skanyxx.Core.Platform.Preferences;

/// <summary>The organization's default preferences (owner only at the API).</summary>
public sealed record GetOrganizationPreferencesQuery : IRequest<Outcome<PreferenceSet>>;
