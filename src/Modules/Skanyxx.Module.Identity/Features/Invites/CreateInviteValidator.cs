using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Invites;

internal sealed class CreateInviteValidator : AbstractValidator<CreateInviteCommand>
{
    public CreateInviteValidator(IOptions<IdentityOptions> identity)
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.Email).NotEmpty().MaximumLength(256).EmailAddress().NoControlCharacters().UserNameCharacters(identity);
        RuleFor(c => c.Roles).NotEmpty().GrantableRoles();
    }
}
