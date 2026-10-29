using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Org;

internal sealed class DeleteDepartmentValidator : AbstractValidator<DeleteDepartmentCommand>
{
    public DeleteDepartmentValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.Slug).OrgSlug();
        RuleFor(c => c.MoveTeamsTo!).OrgSlug().When(c => c.MoveTeamsTo is not null);
    }
}
