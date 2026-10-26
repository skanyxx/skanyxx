using FluentValidation;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Features;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>Messages name the header, never the value.</summary>
internal sealed class SetPluginServerSecretsValidator : AbstractValidator<SetPluginServerSecretsCommand>
{
    public SetPluginServerSecretsValidator()
    {
        RuleFor(c => c.User).NotNull().SetValidator(new StudioUserValidator());
        RuleFor(c => c.Server).Must(StudioNames.IsValidServerName).WithMessage("Invalid server name.");
        RuleFor(c => c.Values).NotNull()
            .Must(v => v.Count is > 0 and <= PluginRules.MaxHeaders).WithMessage($"1 to {PluginRules.MaxHeaders} header values.")
            .Must(v => v.Keys.All(k => k is not null && PluginRules.IsHeaderName(k))).WithMessage($"Header names: {PluginRules.HeaderNameRule}")
            .Must(v => v.Values.All(value => value is not null && PluginRules.IsHeaderValue(value)))
            .WithMessage($"Each value: at most {PluginRules.MaxHeaderValue} characters, without control characters.")
            .Must(v => v.Keys.Select(k => k.ToLowerInvariant()).Distinct().Count() == v.Count).WithMessage("Each header once.")
            .When(c => c.Values is not null);
    }
}
