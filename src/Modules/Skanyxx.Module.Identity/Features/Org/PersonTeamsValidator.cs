using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Org;

internal sealed class PersonTeamsValidator : AbstractValidator<PersonTeamsQuery>
{
    public PersonTeamsValidator() => RuleFor(q => q.UserId).NotEmpty().MaximumLength(450).NoControlCharacters();
}
