using FluentValidation;
using Skanyxx.Module.Agents.Studio.Features;
using Skanyxx.Module.Agents.Studio.Plugins;

namespace Skanyxx.Module.Agents.Plugins.Features;

internal sealed class InspectPluginValidator : AbstractValidator<InspectPluginQuery>
{
    public InspectPluginValidator()
    {
        RuleFor(q => q.User).NotNull().SetValidator(new StudioUserValidator());
        // Someone who may not read that source gets the handler's 403, whatever the request holds.
        RuleFor(q => q.Request).NotNull().SetValidator(q => PluginSourceRequestRules.For(q.Upload is not null))
            .When(q => q.User is not null && q.Request is not null && PluginAccess.CanInspect(q.User, q.Request.Source));
    }
}
