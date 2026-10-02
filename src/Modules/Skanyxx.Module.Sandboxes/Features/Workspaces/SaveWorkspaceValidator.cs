using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes.Features.Workspaces;

internal sealed partial class SaveWorkspaceValidator : AbstractValidator<SaveWorkspaceCommand>
{
    public SaveWorkspaceValidator(IOptions<SandboxesOptions> options)
    {
        RuleFor(c => c.UserId).ValidUser();
        RuleFor(c => c.Name).ValidName();

        RuleFor(c => c.Git.Count).LessThanOrEqualTo(SaveWorkspaceCommand.MaxGit).OverridePropertyName("git");
        RuleForEach(c => c.Git).NotNull().ChildRules(g =>
        {
            // https without credentials: AX has no secret field, and a token in the URL would be readable by every caller.
            g.RuleFor(r => r.Repo).Must(r => r is not null && Uri.TryCreate(r, UriKind.Absolute, out var u)
                    && u.Scheme == Uri.UriSchemeHttps && u.UserInfo.Length == 0)
                .WithMessage("A repo is an https URL without credentials.");
            g.RuleFor(r => r.Name).Must(DnsLabel.IsValid).When(r => r.Name is not null).WithMessage("Git names are DNS labels.");
            g.RuleFor(r => r.Branch).Must(b => Branch().IsMatch(b!)).When(r => r.Branch is not null).WithMessage("Invalid branch.");
            g.RuleFor(r => r.Dir).Must(d => RelativeDir().IsMatch(d!) && !d!.Split('/').Contains("..")).When(r => r.Dir is not null)
                .WithMessage("A dir is a relative path without '..'.");
            g.RuleFor(r => r.Depth).InclusiveBetween(0, 100_000);
        }).OverridePropertyName("git");

        RuleFor(c => c.McpServers.Count).LessThanOrEqualTo(SaveWorkspaceCommand.MaxMcpServers).OverridePropertyName("mcpServers");
        RuleForEach(c => c.McpServers).NotNull().ChildRules(s =>
        {
            s.RuleFor(m => m.Name).Must(n => DnsLabel.IsValid(n) && n != AxMapper.MemoryServerName)
                .WithMessage($"MCP server names are DNS labels; '{AxMapper.MemoryServerName}' is set by attachMemory.");
            s.RuleFor(m => m.Endpoint).Must(e => e is not null && SandboxesOptions.IsHttpUrl(e))
                .WithMessage("An MCP endpoint is an absolute http(s) URL without credentials.");
        }).OverridePropertyName("mcpServers");

        RuleFor(c => c.AttachMemory).Must(attach => !attach || options.Value.MemoryMcpUrl.Length > 0).OverridePropertyName("attachMemory")
            .WithMessage("Memory attach is off: Sandboxes:MemoryMcpUrl is not configured.");
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/-]{0,254}\z")]
    private static partial Regex Branch();

    [GeneratedRegex(@"^[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)*\z")]
    private static partial Regex RelativeDir();
}
