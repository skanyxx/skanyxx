using FluentValidation;

namespace Skanyxx.Module.Investigate.Features.Options;

internal sealed class GetOptionsValidator : AbstractValidator<GetOptionsQuery>
{
    public GetOptionsValidator() => RuleFor(q => q.Viewer).SignedIn();
}
