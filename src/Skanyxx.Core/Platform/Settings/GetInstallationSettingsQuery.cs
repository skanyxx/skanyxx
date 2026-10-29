using MediatR;

namespace Skanyxx.Core.Platform.Settings;

/// <summary>The effective value of every setting (saved, else deployment configuration, else the default). Secrets are never returned.</summary>
public sealed record GetInstallationSettingsQuery : IRequest<Outcome<IReadOnlyDictionary<string, string>>>;
