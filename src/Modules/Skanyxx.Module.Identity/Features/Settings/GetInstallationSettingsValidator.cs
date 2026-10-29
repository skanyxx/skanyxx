using FluentValidation;
using Skanyxx.Core.Platform.Settings;

namespace Skanyxx.Module.Identity.Features.Settings;

/// <summary>Nothing to check; present because every request has a validator (the architecture tests).</summary>
internal sealed class GetInstallationSettingsValidator : AbstractValidator<GetInstallationSettingsQuery>;
