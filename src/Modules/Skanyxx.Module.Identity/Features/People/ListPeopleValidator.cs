using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.People;

/// <summary>Nothing to check; present because every request has exactly one validator.</summary>
internal sealed class ListPeopleValidator : AbstractValidator<ListPeopleQuery>;
