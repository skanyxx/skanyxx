using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Host.Pages;

/// <summary>
/// The library (D009, D048): any signed-in person searches and opens the cards they may read, lifts a published one into
/// a higher scope they may write, and renames a key where they may write (D038). Every rule is the memory module's
/// (its <c>AccessPolicy</c>, through the same commands as <c>api/memory/cards</c>); this page only shows what it answers.
/// No folder tree.
/// </summary>
public sealed class LibraryModel(IMediator mediator) : PageModel
{
    /// <summary>Search text: any word matches.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>One scope to look in; empty means every scope this person may read.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    public LibraryShelf Shelf { get; private set; } = new([], []);

    public LibraryCard? Opened { get; private set; }

    /// <summary>What the last action did; carried across its redirect (post/redirect/get).</summary>
    [TempData]
    public string? Message { get; set; }

    /// <summary>The first refusal on this request; its status is the response's.</summary>
    public string? Error { get; private set; }

    private string? Me => Caller.UserId(User);

    /// <summary>The person's own personal scope reads as theirs; scope ids are otherwise shown as they are.</summary>
    public string ScopeLabel(string scope) => scope == $"personal:{Me}" ? "personal (you)" : scope;

    public string WhoLabel(string who) => who == Me ? "you" : who;

    public async Task OnGetAsync(string? scope, string? key, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(scope) && !string.IsNullOrEmpty(key))
            await ValidatedAsync(() => OpenAsync(scope, key, ct));
        await BrowseAsync(ct);
    }

    public Task<IActionResult> OnPostLiftAsync(string? scope, string? key, string? target, CancellationToken ct) =>
        ActAsync(scope, key, () => mediator.Send(new LiftCardCommand(LibraryUser.From(User), scope ?? "", key ?? "", target ?? ""), ct),
            OutcomeStatus.Created, card => $"Lifted to {card.Scope}. The original stays where it was.", ct);

    public Task<IActionResult> OnPostRenameAsync(string? scope, string? key, string? newKey, int version, CancellationToken ct) =>
        ActAsync(scope, key,
            () => mediator.Send(new RenameCardCommand(LibraryUser.From(User), scope ?? "", key ?? "", newKey?.Trim() ?? "", version), ct),
            OutcomeStatus.Ok, card => $"Renamed to {card.Key}.", ct);

    /// <summary>
    /// Success redirects to the card it produced (post/redirect/get: a refresh re-sends nothing). A refusal renders here
    /// with its status, the card as it is now (if this person may still open it) and the list.
    /// </summary>
    private async Task<IActionResult> ActAsync(
        string? scope, string? key, Func<Task<Outcome<CardDto>>> send, OutcomeStatus success, Func<CardDto, string> message, CancellationToken ct)
    {
        var outcome = await ValidatedAsync(send);
        if (outcome is { Value: { } card } && outcome.Status == success)
        {
            Message = message(card);
            return RedirectToPage(null, null, new { scope = card.Scope, key = card.Key, q = Q, filter = Filter }, "card");
        }

        if (outcome is not null)
        {
            Fail(outcome);
            if (!string.IsNullOrEmpty(scope) && !string.IsNullOrEmpty(key))
                Opened = (await mediator.Send(new OpenCardQuery(LibraryUser.From(User), scope, key), ct)).Value;
        }
        await BrowseAsync(ct);
        return Page();
    }

    private async Task<bool> OpenAsync(string scope, string key, CancellationToken ct)
    {
        var outcome = await mediator.Send(new OpenCardQuery(LibraryUser.From(User), scope, key), ct);
        Opened = outcome.Value;
        if (Opened is null)
            Fail(outcome);
        return Opened is not null;
    }

    private async Task BrowseAsync(CancellationToken ct)
    {
        var shelf = await ValidatedAsync(() => mediator.Send(new BrowseLibraryQuery(LibraryUser.From(User), Q?.Trim(), Filter), ct));
        if (shelf?.Value is { } value)
            Shelf = value;
        else if (shelf is not null)
            Fail(shelf);
    }

    private void Fail<T>(Outcome<T> outcome) => Fail(outcome.Message, outcome.Status.HttpStatus());

    /// <summary>The first failure wins: a list that also fails must not hide why the action was refused.</summary>
    private void Fail(string? message, int status)
    {
        if (Error is not null)
            return;
        Error = message;
        Response.StatusCode = status;
    }

    /// <summary>A validation failure (a malformed scope or key) answers 400 with its messages; the result is then null.</summary>
    private async Task<T?> ValidatedAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (ValidationException ex)
        {
            Fail(string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct()), StatusCodes.Status400BadRequest);
            return default;
        }
    }
}
