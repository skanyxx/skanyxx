using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Org;

/// <summary>Nothing to check; present because every request has exactly one validator.</summary>
internal sealed class ListDepartmentsValidator : AbstractValidator<ListDepartmentsQuery>;
