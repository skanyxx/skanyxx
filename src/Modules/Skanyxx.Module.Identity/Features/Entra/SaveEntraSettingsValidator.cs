using FluentValidation;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Features.Entra;

/// <summary>
/// Ids are GUIDs (tenant, client, groups — D5); turning sign-in on needs a tenant, a client and at least one group, and
/// every group must give a role or a team. Roles never include <c>owner</c>. Whether a secret is stored and whether the
/// teams exist is the handler's to check.
/// </summary>
internal sealed class SaveEntraSettingsValidator : AbstractValidator<SaveEntraSettingsCommand>
{
    public const int MaxGroups = 200;

    public SaveEntraSettingsValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.TenantId).Must(BeEmptyOrGuid).WithMessage("'Tenant Id' must be the directory (tenant) id, a GUID.");
        RuleFor(c => c.ClientId).Must(BeEmptyOrGuid).WithMessage("'Client Id' must be the application (client) id, a GUID.");
        RuleFor(c => c.ClientSecret).MaximumLength(1024).NoControlCharacters();
        When(c => c.Enabled, () =>
        {
            RuleFor(c => c.TenantId).NotEmpty().WithMessage("Enter the directory (tenant) id to turn Microsoft sign-in on.");
            RuleFor(c => c.ClientId).NotEmpty().WithMessage("Enter the application (client) id to turn Microsoft sign-in on.");
            RuleFor(c => c.Groups).NotEmpty().WithMessage("Map at least one group: people in no mapped group cannot sign in.");
        });
        RuleFor(c => c.Groups).Must(g => g.Count <= MaxGroups).WithMessage($"At most {MaxGroups} groups can be mapped.")
            .Must(g => g.Select(x => EntraClaims.NormalizeId(x.GroupId)).OfType<string>().GroupBy(id => id).All(same => same.Count() == 1))
            .WithMessage("Each group can be mapped once.");
        RuleForEach(c => c.Groups).ChildRules(group =>
        {
            group.RuleFor(g => g.GroupId).Must(id => Guid.TryParse(id, out _)).WithMessage("A group id must be the group's object id, a GUID.");
            group.RuleFor(g => g.Label).DisplayName();
            group.RuleFor(g => g.Roles).GrantableRoles();
            group.RuleForEach(g => g.Teams).OrgSlug();
            group.RuleFor(g => g).Must(g => g.Roles.Count + g.Teams.Count > 0).WithName("Group").WithMessage("Each mapped group must give a role or a team.");
        });
    }

    private static bool BeEmptyOrGuid(string? value) => string.IsNullOrEmpty(value) || Guid.TryParse(value, out _);
}
