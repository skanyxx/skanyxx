using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.People;

internal sealed class SetDisabledValidator : AbstractValidator<SetDisabledCommand>
{
    public SetDisabledValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.UserId).NotEmpty().MaximumLength(450).NoControlCharacters();
    }
}
