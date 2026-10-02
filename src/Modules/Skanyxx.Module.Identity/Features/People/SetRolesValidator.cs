using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.People;

/// <summary>An empty list is allowed: the person keeps signing in with no hat (the owner keeps theirs).</summary>
internal sealed class SetRolesValidator : AbstractValidator<SetRolesCommand>
{
    public SetRolesValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.UserId).NotEmpty().MaximumLength(450).NoControlCharacters();
        RuleFor(c => c.Roles).GrantableRoles();
    }
}
