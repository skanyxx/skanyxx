using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.People;

internal sealed class RemoveEntraLoginValidator : AbstractValidator<RemoveEntraLoginCommand>
{
    public RemoveEntraLoginValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.UserId).NotEmpty().MaximumLength(450).NoControlCharacters();
    }
}
