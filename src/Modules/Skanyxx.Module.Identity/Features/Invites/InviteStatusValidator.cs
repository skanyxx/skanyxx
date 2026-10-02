using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Invites;

internal sealed class InviteStatusValidator : AbstractValidator<InviteStatusQuery>
{
    public InviteStatusValidator() => RuleFor(q => q.Token).NotEmpty().MaximumLength(128).NoControlCharacters();
}
