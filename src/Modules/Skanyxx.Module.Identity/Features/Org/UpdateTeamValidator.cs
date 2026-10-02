using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Org;

internal sealed class UpdateTeamValidator : AbstractValidator<UpdateTeamCommand>
{
    public UpdateTeamValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.Slug).OrgSlug();
        RuleFor(c => c.Name).OrgName();
        RuleFor(c => c.Department).OrgSlug();
    }
}
