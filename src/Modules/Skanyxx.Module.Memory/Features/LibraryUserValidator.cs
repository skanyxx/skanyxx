using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features;

/// <summary>Shared child rules for the library's requests; each request still has exactly one top-level validator.</summary>
internal sealed class LibraryUserValidator : AbstractValidator<LibraryUser>
{
    public LibraryUserValidator() =>
        RuleFor(u => u.UserId).Cascade(CascadeMode.Stop)
            .NotNull().WithName("identity").WithMessage("A signed-in user is required.")
            .Must(Scope.IsValidId).WithName("userId").WithMessage("Invalid user id.");
}
