using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Pages;

/// <summary>
/// The owner's org tree (D055): departments, their teams and each team's members. The same commands as
/// <c>api/identity/org</c>. Memory's team and department scopes key on these slugs, which never change.
/// </summary>
[Authorize(Roles = SkanyxxRoles.Owner)]
public sealed class OrgModel(IMediator mediator) : PageModel
{
    public IReadOnlyList<DepartmentDto> Departments { get; private set; } = [];

    public IReadOnlyList<TeamDto> Teams { get; private set; } = [];

    public IReadOnlyList<PersonDto> People { get; private set; } = [];

    private Dictionary<string, PersonDto> _peopleById = [];

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public string NameOf(string userId) => _peopleById.TryGetValue(userId, out var p) ? p.DisplayName ?? p.Email : userId;

    /// <summary>A disabled member keeps the membership (it grants nothing without a session) and has it again once enabled.</summary>
    public bool IsDisabled(string userId) => _peopleById.TryGetValue(userId, out var p) && p.Disabled;

    /// <summary>Their teams come from the Entra group mapping and are rewritten at each Microsoft sign-in (D1).</summary>
    public bool IsEntraManaged(string userId) => _peopleById.TryGetValue(userId, out var p) && p.EntraManaged;

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostCreateDepartmentAsync(string? slug, string? name, CancellationToken ct) =>
        await RunAsync(async () => Report(await mediator.Send(new CreateDepartmentCommand(Actor, slug ?? "", name?.Trim() ?? ""), ct),
            "Department created."), ct);

    public async Task<IActionResult> OnPostRenameDepartmentAsync(string? slug, string? name, CancellationToken ct) =>
        await RunAsync(async () => Report(await mediator.Send(new RenameDepartmentCommand(Actor, slug ?? "", name?.Trim() ?? ""), ct),
            "Department renamed."), ct);

    public async Task<IActionResult> OnPostCreateTeamAsync(string? slug, string? name, string? department, CancellationToken ct) =>
        await RunAsync(async () => Report(await mediator.Send(new CreateTeamCommand(Actor, slug ?? "", name?.Trim() ?? "", department ?? ""), ct),
            "Team created."), ct);

    public async Task<IActionResult> OnPostUpdateTeamAsync(string? slug, string? name, string? department, CancellationToken ct) =>
        await RunAsync(async () => Report(await mediator.Send(new UpdateTeamCommand(Actor, slug ?? "", name?.Trim() ?? "", department ?? ""), ct),
            "Team saved."), ct);

    public async Task<IActionResult> OnPostAddMemberAsync(string? slug, string? userId, CancellationToken ct) =>
        await RunAsync(async () => Report(await mediator.Send(new SetTeamMemberCommand(Actor, slug ?? "", userId ?? "", Member: true), ct),
            "Member added."), ct);

    public async Task<IActionResult> OnPostRemoveMemberAsync(string? slug, string? userId, CancellationToken ct) =>
        await RunAsync(async () => Report(await mediator.Send(new SetTeamMemberCommand(Actor, slug ?? "", userId ?? "", Member: false), ct),
            "Member removed."), ct);

    private string Actor => Caller.UserId(User)!;

    private void Report<T>(Outcome<T> outcome, string done)
    {
        if (outcome.Status is OutcomeStatus.Ok or OutcomeStatus.Created)
            Message = done;
        else
        {
            Error = outcome.Message;
            Response.StatusCode = outcome.Status.HttpStatus();
        }
    }

    private async Task<IActionResult> RunAsync(Func<Task> action, CancellationToken ct)
    {
        try
        {
            await action();
        }
        catch (ValidationException ex)
        {
            Error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct());
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Departments = (await mediator.Send(new ListDepartmentsQuery(), ct)).Value!;
        Teams = (await mediator.Send(new ListTeamsQuery(), ct)).Value!;
        People = (await mediator.Send(new ListPeopleQuery(), ct)).Value!;
        _peopleById = People.ToDictionary(p => p.Id);
    }
}
