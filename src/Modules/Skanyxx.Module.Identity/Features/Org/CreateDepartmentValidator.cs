using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Org;

internal sealed class CreateDepartmentValidator : AbstractValidator<CreateDepartmentCommand>
{
    public CreateDepartmentValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.Slug).OrgSlug();
        RuleFor(c => c.Name).OrgName();
    }
}
