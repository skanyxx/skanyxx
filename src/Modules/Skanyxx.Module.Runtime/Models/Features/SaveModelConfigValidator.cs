using FluentValidation;
using Skanyxx.Core.Platform.Runtime;
using Skanyxx.Module.Runtime.Models.Definition;

namespace Skanyxx.Module.Runtime.Models.Features;

/// <summary>The request's shape; the fields against kagent's providers are the handler's (<see cref="ModelParams"/>).</summary>
internal sealed class SaveModelConfigValidator : AbstractValidator<SaveModelConfigCommand>
{
    public SaveModelConfigValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.Namespace).Must(ModelRefs.IsKubeName)
            .WithMessage("'Namespace' must be a Kubernetes name: lowercase letters, digits and '-', at most 63.");
        RuleFor(c => c.Name).Must(ModelRefs.IsKubeName)
            .WithMessage("'Name' must be a Kubernetes name: lowercase letters, digits and '-', at most 63.");
        RuleFor(c => c.Provider).Must(ProviderCatalog.All.ContainsKey)
            .WithMessage($"'Provider' must be one of: {string.Join(", ", ProviderCatalog.All.Keys)}.");
        // Model ids look like qwen3-coder:30b, gpt-4.1, claude-sonnet-4-5, org/model@rev, us.anthropic.claude-…-v1:0.
        RuleFor(c => c.Model).NotEmpty().MaximumLength(200).Matches("^[A-Za-z0-9][A-Za-z0-9._:/@+-]*$")
            .WithMessage("'Model' must be a model id: letters, digits and . _ : / @ + - only.");
        RuleFor(c => c.ApiKey).MaximumLength(4096)
            .Must(k => k is null || !k.Any(char.IsWhiteSpace) && !k.Any(char.IsControl))
            .WithMessage("'Api Key' must not contain spaces, line breaks or control characters.");
        RuleFor(c => c.ApiKey).Empty().When(c => c.Provider == ProviderCatalog.Ollama).WithMessage("Ollama takes no API key.");
        RuleFor(c => c.ApiKey).Empty().When(c => c.NoApiKey).WithMessage("Paste a key or choose 'No API key', not both.");
        RuleFor(c => c.Params.Count).LessThanOrEqualTo(40).OverridePropertyName("Params");
        RuleForEach(c => c.Params).Must(p => p.Key.Trim() is { Length: > 0 and <= 64 } key && key.All(char.IsAsciiLetterOrDigit))
            .WithMessage("A field name must be letters and digits only.");
        RuleFor(c => c.Params).Must(p => p.Keys.Select(k => k.Trim()).Distinct(StringComparer.Ordinal).Count() == p.Count)
            .WithMessage("Each field may be given once.");
        RuleForEach(c => c.Params).Must(p => p.Value.Length <= 2000 && !p.Value.Any(char.IsControl))
            .WithMessage("A field value must be one line of at most 2000 characters.");
    }
}
