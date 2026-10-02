using FluentValidation;

namespace Skanyxx.Module.Tickets.Features.Runs;

internal sealed class GetRunValidator : AbstractValidator<GetRunQuery>
{
    public GetRunValidator()
    {
        RuleFor(q => q.UserId).ValidUser();
        RuleFor(q => q.Id).ValidRunId();
    }
}
