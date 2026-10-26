using FluentValidation;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Module.Agents.Studio.Features;

namespace Skanyxx.Module.Agents.Studio.Plugins;

internal sealed class PluginsOverviewValidator : AbstractValidator<PluginsOverviewQuery>
{
    public PluginsOverviewValidator() => RuleFor(q => q.User).NotNull().SetValidator(new StudioUserValidator());
}
