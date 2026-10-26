using System.Text.RegularExpressions;
using FluentValidation;
using Skanyxx.Module.Agents.Plugins.Format;

namespace Skanyxx.Module.Agents.Plugins.Features;

/// <summary>
/// The shape of a <see cref="PluginSourceRequest"/>: only the fields its source uses, each bounded. Built per request
/// (not a scanned validator class), so endpoints never run it before the role check.
/// </summary>
internal static partial class PluginSourceRequestRules
{
    public const int MaxUrl = 2048;

    public const int MaxPath = 512;

    public static InlineValidator<PluginSourceRequest> For(bool hasUpload)
    {
        var v = new InlineValidator<PluginSourceRequest>();
        v.RuleFor(r => r.Source).Must(s => s is PluginSourceKinds.Catalog or PluginSourceKinds.Git or PluginSourceKinds.Zip)
            .WithMessage("source is catalog, git or zip.");
        v.RuleFor(r => r.Name).Must(PluginNames.IsValidPluginName).When(r => r.Name is not null).WithMessage("Invalid plugin name.");
        v.RuleFor(r => r.Digest).Matches(Sha256()).When(r => r.Digest is not null).WithMessage("digest is the 64-character SHA-256 inspect answered.");
        v.RuleFor(r => r.Path).MaximumLength(MaxPath).Must(p => !p!.StartsWith('/') && !p.Contains('\\') && p.Split('/').All(s => s is not ("." or "..")))
            .When(r => r.Path is not null).WithMessage($"path is a folder inside the source: relative, '/'-separated, at most {MaxPath} characters.");
        v.RuleFor(r => r).Must(r => r.Url is null && r.Commit is null && r.Path is null).When(r => r.Source == PluginSourceKinds.Catalog)
            .WithName("source").WithMessage("The catalog takes only a plugin name.");
        v.RuleFor(r => r.Url).NotEmpty().MaximumLength(MaxUrl).When(r => r.Source == PluginSourceKinds.Git)
            .WithMessage($"url is the repository's https URL (at most {MaxUrl} characters).");
        v.RuleFor(r => r.Commit).NotNull().Matches(Sha1()).When(r => r.Source == PluginSourceKinds.Git)
            .WithMessage("commit is the full 40-character lowercase SHA.");
        v.RuleFor(r => r).Must(r => hasUpload == (r.Source == PluginSourceKinds.Zip) && (!hasUpload || r.Url is null && r.Commit is null))
            .WithName("source").WithMessage("A zip source is an upload (api/plugins/inspect/zip, install/zip); the others are not.");
        return v;
    }

    [GeneratedRegex("^[0-9a-f]{40}\\z")]
    private static partial Regex Sha1();

    [GeneratedRegex("^[0-9a-f]{64}\\z")]
    private static partial Regex Sha256();
}
