using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Org;

internal sealed class RenameDepartmentValidator : AbstractValidator<RenameDepartmentCommand>
{
    public RenameDepartmentValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.Slug).OrgSlug();
        RuleFor(c => c.Name).OrgName();
    }
}
