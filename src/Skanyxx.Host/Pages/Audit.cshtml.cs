using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Pages;

/// <summary>The identity audit (D152), the owner's only: newest first, filtered by action or person, paged with "older".</summary>
[Authorize(Roles = SkanyxxRoles.Owner)]
public sealed class AuditModel(IMediator mediator) : PageModel
{
    public const int PageSize = 100;

    [BindProperty(SupportsGet = true)]
    public string? Action { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? Before { get; set; }

    public IReadOnlyList<AuditEntryDto> Entries { get; private set; } = [];

    public string? Error { get; private set; }

    private IReadOnlyDictionary<string, string> _names = new Dictionary<string, string>();

    /// <summary>A user id as the person's email when it is one; anything else (an invite id, a slug, "entra-mapping") as it is.</summary>
    public string Name(string? id) => id is null ? "" : _names.TryGetValue(id, out var email) ? email : id;

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            Entries = (await mediator.Send(new ListAuditQuery(Before, PageSize, NullIfEmpty(Action), NullIfEmpty(UserId)), ct)).Value!;
        }
        catch (ValidationException ex)
        {
            Error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct());
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        _names = (await mediator.Send(new ListPeopleQuery(), ct)).Value!.ToDictionary(p => p.Id, p => p.Email);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
