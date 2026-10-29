using FluentValidation;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Investigate.Features.Runs;

internal sealed class StartRunValidator : AbstractValidator<StartRunCommand>
{
    public StartRunValidator(IOptions<InvestigateOptions> options)
    {
        var max = options.Value.MaxInputChars;
        RuleFor(c => c.Viewer).SignedIn();
        RuleFor(c => c.Input).Must(i => !string.IsNullOrWhiteSpace(i) && i.Length <= max && !i.Contains('\0'))
            .OverridePropertyName("input").WithMessage($"Describe what to investigate, in at most {max} characters.");
    }
}
