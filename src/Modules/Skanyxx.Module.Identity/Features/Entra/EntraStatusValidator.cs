using FluentValidation;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Entra;

internal sealed class EntraStatusValidator : AbstractValidator<EntraStatusQuery>
{
    public EntraStatusValidator() => RuleFor(q => q.UserId).MaximumLength(450);
}
