using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

internal sealed partial class RunSandboxValidator : AbstractValidator<RunSandboxCommand>
{
    public RunSandboxValidator(IOptions<SandboxesOptions> options)
    {
        var settings = options.Value;
        RuleFor(c => c.UserId).ValidUser();
        RuleFor(c => c.Name).ValidName();
        RuleFor(c => c.Name).Must(n => !RunSandboxCommand.ReservedNames.Contains(n)).OverridePropertyName("name")
            .WithMessage($"'{string.Join("', '", RunSandboxCommand.ReservedNames)}' are reserved (routes under /api/sandboxes, or the old one for tasks); choose another name.");
        RuleFor(c => c.Image).Cascade(CascadeMode.Stop).NotNull().OverridePropertyName("image")
            .Must(i => ImageRef().IsMatch(i)).OverridePropertyName("image")
            .WithMessage("An image reference is required (registry/repo[:tag][@sha256:…]).")
            .Must(i => ImagePolicy.IsAllowed(i, settings)).OverridePropertyName("image")
            .WithMessage("This image may not run here (Sandboxes:AllowedImages, Sandboxes:RequireDigest).");

        RuleFor(c => c.Command.Count).LessThanOrEqualTo(RunSandboxCommand.MaxCommandArgs).OverridePropertyName("command");
        RuleForEach(c => c.Command).NotNull().MaximumLength(RunSandboxCommand.MaxValueChars).NoNul().OverridePropertyName("command");

        RuleFor(c => c.Env.Count).LessThanOrEqualTo(RunSandboxCommand.MaxEnv).OverridePropertyName("env");
        RuleForEach(c => c.Env.Keys).Must(k => EnvName().IsMatch(k)).OverridePropertyName("env")
            .WithMessage("Env names are letters, digits and '_', not starting with a digit.");
        RuleForEach(c => c.Env.Keys).Must(k => !SandboxEnv.IsReserved(k)).OverridePropertyName("env")
            .WithMessage($"Env names starting with {SandboxEnv.ReservedPrefix} are set by Skanyxx.");
        RuleForEach(c => c.Env.Values).NotNull().MaximumLength(RunSandboxCommand.MaxValueChars).NoNul().OverridePropertyName("env");

        RuleFor(c => c.Workspaces.Count).LessThanOrEqualTo(RunSandboxCommand.MaxWorkspaces).OverridePropertyName("workspaces");
        RuleForEach(c => c.Workspaces).NotNull().ChildRules(w =>
        {
            w.RuleFor(m => m.Name).Must(DnsLabel.IsValid).WithMessage("Workspace names are DNS labels.");
            w.RuleFor(m => m.Path).Must(IsWorkspacePath).When(m => m.Path is not null)
                .WithMessage("A path is /workspace or below it, without '..'.");
            w.RuleFor(m => m.Goal).MaximumLength(RunSandboxCommand.MaxGoalChars).NoNul();
        }).OverridePropertyName("workspaces");

        RuleFor(c => c.Resources).Custom((resources, context) =>
        {
            if (ResourcePolicy.Problem(resources, settings) is { } problem)
                context.AddFailure("resources", problem);
        });
    }

    private static bool IsWorkspacePath(string? path) =>
        path is not null && WorkspacePath().IsMatch(path) && !path.Split('/').Contains("..");

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/:@+-]{0,511}\z")]
    private static partial Regex ImageRef();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,127}\z")]
    private static partial Regex EnvName();

    [GeneratedRegex(@"^/workspace(/[A-Za-z0-9._-]+)*\z")]
    private static partial Regex WorkspacePath();
}
