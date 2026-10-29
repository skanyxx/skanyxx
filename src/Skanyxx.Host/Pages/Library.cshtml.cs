using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Host.Pages;

/// <summary>
/// The library (D009, D048): any signed-in person searches and opens the cards they may read, lifts a published one into
/// a higher scope they may write, and renames a key where they may write (D038). Every rule is the memory module's
/// (its <c>AccessPolicy</c>, through the same commands as <c>api/memory/cards</c>); this page only shows what it answers.
/// Filtering, sorting, counts and paging are the server's (D355), with the whole state in the URL. No folder tree.
/// </summary>
public sealed class LibraryModel(IMediator mediator) : PageModel
{
    /// <summary>Search text: any word matches.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>One scope to look in (a team, a department, …); empty means every scope this person may read.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    // D355: the rest of the list's state, all in the URL so a filtered page can be bookmarked, shared and reloaded.
    [BindProperty(SupportsGet = true)]
    public string? Type { get; set; }

    /// <summary>A card keyword (memory's tags).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Tag { get; set; }

    /// <summary>The card's last writer (<c>who</c>).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Owner { get; set; }

    /// <summary>D506: the shelf's other filters (the API has them): a ticket key, the agent that wrote, where a card came from.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Ticket { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Agent { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Origin { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Offset { get; set; }

    public LibraryShelf Shelf { get; private set; } = new([], []);

    /// <summary>False when the list itself could not be read: the page then says so instead of showing an empty shelf.</summary>
    public bool ShelfLoaded { get; private set; }

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

    public string? MyId => Me;

    public bool Filtered =>
        !string.IsNullOrWhiteSpace(Q) || !string.IsNullOrEmpty(Filter) || !string.IsNullOrEmpty(Type) || !string.IsNullOrEmpty(Tag)
        || !string.IsNullOrEmpty(Owner) || !string.IsNullOrEmpty(Ticket) || !string.IsNullOrEmpty(Agent) || !string.IsNullOrEmpty(Origin)
        || From is not null || To is not null;

    /// <summary>
    /// The list's state as route values, for every link and form that should come back to the same list; each change
    /// given in <paramref name="change"/> replaces one value (null drops it). Any filter change starts at the first page.
    /// </summary>
    public Dictionary<string, string> ListState(params (string Key, string? Value)[] change)
    {
        var state = new Dictionary<string, string?>
        {
            ["q"] = Q, ["filter"] = Filter, ["type"] = Type, ["tag"] = Tag, ["owner"] = Owner,
            ["ticket"] = Ticket, ["agent"] = Agent, ["origin"] = Origin,
            ["from"] = From?.ToString("yyyy-MM-dd"), ["to"] = To?.ToString("yyyy-MM-dd"), ["sort"] = Sort,
            ["offset"] = Offset > 0 ? Offset.ToString() : null
        };
        if (change.Length > 0 && change.All(c => c.Key != "offset"))
            state["offset"] = null;
        foreach (var (key, value) in change)
            state[key] = value;
        return state.Where(kv => !string.IsNullOrEmpty(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value!);
    }

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

    /// <summary>D507: one's own personal card, or any card for the owner (memory decides; this only offers the button).</summary>
    public bool CanDelete(CardDto card) => card.Scope == $"personal:{Me}" || User.IsInRole(SkanyxxRoles.Owner);

    /// <summary>Success goes back to the list (the card is gone); a refusal renders like the other actions'.</summary>
    public async Task<IActionResult> OnPostDeleteAsync(string? scope, string? key, int version, CancellationToken ct)
    {
        var outcome = await ValidatedAsync(() => mediator.Send(new DeleteCardCommand(LibraryUser.From(User), scope ?? "", key ?? "", version), ct));
        if (outcome is { Status: OutcomeStatus.Ok, Value: { } card })
        {
            Message = $"Deleted {card.Scope}/{card.Key} and its history.";
            return RedirectToPage(null, null, new RouteValueDictionary(ListState().ToDictionary(kv => kv.Key, kv => (object?)kv.Value)));
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
            var route = new RouteValueDictionary { ["scope"] = card.Scope, ["key"] = card.Key };
            foreach (var (name, value) in ListState())
                route[name] = value;
            return RedirectToPage(null, null, route, "card");
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
        var filter = new LibraryFilter(Ticket: Blank(Ticket)?.ToUpperInvariant(), Keyword: Blank(Tag), Type: Blank(Type), Origin: Blank(Origin),
            Agent: Blank(Agent), Offset: Offset, Owner: Blank(Owner), From: From, To: To, Sort: Blank(Sort));
        var shelf = await ValidatedAsync(() => mediator.Send(new BrowseLibraryQuery(LibraryUser.From(User), Q?.Trim(), Filter, filter), ct));
        if (shelf?.Value is { } value)
        {
            Shelf = value;
            ShelfLoaded = true;
        }
        else if (shelf is not null)
            Fail(shelf);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
