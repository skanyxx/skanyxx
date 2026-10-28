using FluentValidation;
using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Module.Identity.Features.Preferences;

/// <summary>Nothing to check: the query has no input. Here so every request has its validator.</summary>
internal sealed class GetOrganizationPreferencesValidator : AbstractValidator<GetOrganizationPreferencesQuery>;
