using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Entra;

/// <summary>Nothing to check; present because every request has exactly one validator.</summary>
internal sealed class GetEntraSettingsValidator : AbstractValidator<GetEntraSettingsQuery>;
