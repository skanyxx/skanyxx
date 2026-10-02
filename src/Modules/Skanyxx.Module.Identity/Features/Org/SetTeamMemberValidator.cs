using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Org;

internal sealed class SetTeamMemberValidator : AbstractValidator<SetTeamMemberCommand>
{
    public SetTeamMemberValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.Team).OrgSlug();
        RuleFor(c => c.UserId).NotEmpty().MaximumLength(450).NoControlCharacters();
    }
}
