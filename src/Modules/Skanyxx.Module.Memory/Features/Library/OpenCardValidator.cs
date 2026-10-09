using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Library;

internal sealed class OpenCardValidator : AbstractValidator<OpenCardQuery>
{
    public OpenCardValidator()
    {
        RuleFor(q => q.User).NotNull().SetValidator(new LibraryUserValidator());
        RuleFor(q => q.Scope).Must(s => Scope.TryParse(s, out _)).WithMessage("Invalid scope.");
        RuleFor(q => q.Key).Must(CardKey.IsValid).WithMessage("Invalid key.");
    }
}
