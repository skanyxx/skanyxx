using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Entra;

internal sealed class LinkEntraValidator : AbstractValidator<LinkEntraCommand>
{
    public LinkEntraValidator() => RuleFor(c => c.UserId).NotEmpty().MaximumLength(450);
}
