using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Org;

internal sealed class DeleteTeamValidator : AbstractValidator<DeleteTeamCommand>
{
    public DeleteTeamValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.Slug).OrgSlug();
        RuleFor(c => c.ReassignTo!).OrgSlug().When(c => c.ReassignTo is not null);
    }
}
