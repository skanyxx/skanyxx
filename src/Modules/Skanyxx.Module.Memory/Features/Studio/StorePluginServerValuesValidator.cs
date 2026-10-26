using FluentValidation;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class StorePluginServerValuesValidator : AbstractValidator<StorePluginServerValuesCommand>
{
    public StorePluginServerValuesValidator()
    {
        RuleFor(c => c.Actor).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Server).Must(PluginServerRules.IsServer).WithMessage("Invalid server name.");
        RuleFor(c => c.Plugin).Must(PluginServerRules.IsPlugin).WithMessage("Invalid plugin name.");
        RuleFor(c => c.Address).NotEmpty().MaximumLength(2048);
        RuleFor(c => c.Values).NotNull()
            .Must(v => v.Count is > 0 and <= PluginServerRules.MaxHeaders).WithMessage($"1 to {PluginServerRules.MaxHeaders} header values.")
            .Must(v => v.All(p => PluginServerRules.IsHeader(p.Key) && PluginServerRules.IsValue(p.Value)))
            .WithMessage($"Header names are letters, digits and '-'; values at most {PluginServerRules.MaxValueChars} characters, no control characters.")
            .Must(v => v.Keys.Select(k => k.ToLowerInvariant()).Distinct().Count() == v.Count).WithMessage("Each header once.")
            .When(c => c.Values is not null);
    }
}
