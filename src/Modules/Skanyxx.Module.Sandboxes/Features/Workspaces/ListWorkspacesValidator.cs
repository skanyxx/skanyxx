using FluentValidation;

namespace Skanyxx.Module.Sandboxes.Features.Workspaces;

internal sealed class ListWorkspacesValidator : AbstractValidator<ListWorkspacesQuery>
{
    public ListWorkspacesValidator() => RuleFor(q => q.UserId).ValidUser();
}
