namespace Skanyxx.Core.Platform.Preferences;

/// <summary>A person's own choices, the organization's defaults they inherit, and the result the layout applies.</summary>
public sealed record PreferencesView(PreferenceSet Saved, PreferenceSet OrganizationDefaults, EffectivePreferences Effective);
