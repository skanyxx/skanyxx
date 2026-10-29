using FluentValidation;

namespace Skanyxx.Module.Investigate.Features.Runs;

internal sealed class GetRunValidator : AbstractValidator<GetRunQuery>
{
    public GetRunValidator() => RuleFor(q => q.Viewer).SignedIn();
}
