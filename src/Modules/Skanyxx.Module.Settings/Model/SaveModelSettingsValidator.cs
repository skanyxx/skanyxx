using FluentValidation;
using Skanyxx.Core.Platform.Runtime;

namespace Skanyxx.Module.Settings.Model;

internal sealed class SaveModelSettingsValidator : AbstractValidator<SaveModelSettingsCommand>
{
    public SaveModelSettingsValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.Provider).Must(ModelProviders.All.Contains).WithMessage($"'Provider' must be one of: {string.Join(", ", ModelProviders.All)}.");
        // Model ids look like qwen3-coder:30b, gpt-4.1, claude-sonnet-4-5, org/model@rev.
        RuleFor(c => c.Model).NotEmpty().MaximumLength(200).Matches("^[A-Za-z0-9][A-Za-z0-9._:/@+-]*$")
            .WithMessage("'Model' must be a model id: letters, digits and . _ : / @ + - only.");
        RuleFor(c => c.ApiKey).MaximumLength(4096)
            .Must(k => k is null || !k.Any(char.IsWhiteSpace) && !k.Any(char.IsControl))
            .WithMessage("'Api Key' must not contain spaces, line breaks or control characters.");
        RuleFor(c => c.ApiKey).Empty().When(c => c.Provider == ModelProviders.Ollama).WithMessage("Ollama takes no API key.");
        // Without a host kagent would fall back to the agent pod's own localhost, which never serves Ollama.
        RuleFor(c => c.BaseUrl).NotEmpty().When(c => c.Provider == ModelProviders.Ollama).WithMessage("Ollama needs its host URL.");
        RuleFor(c => c.BaseUrl).MaximumLength(500).Must(BeEmptyOrHttpUrl)
            .WithMessage("'Base Url' must be an absolute http(s) URL without user info, query or fragment.");
    }

    private static bool BeEmptyOrHttpUrl(string? url) =>
        string.IsNullOrEmpty(url)
        || Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && url == url.Trim();
}
