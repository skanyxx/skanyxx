using FluentValidation;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>Only the request's shape: what is wrong with the form itself is the preview's answer, not a 400.</summary>
internal sealed class ManifestPreviewValidator : AbstractValidator<ManifestPreviewQuery>
{
    public ManifestPreviewValidator()
    {
        RuleFor(q => q.User).NotNull().SetValidator(new StudioUserValidator());
        RuleFor(q => q.Draft).NotNull();
    }
}
