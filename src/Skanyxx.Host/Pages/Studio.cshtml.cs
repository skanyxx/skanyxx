using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Host.Pages;

/// <summary>
/// The studio (studio.md, D028–D032): builders compose an agent in a form (or let the factory draft it) and save it as a
/// pull request; supervisors read the YAML and merge; both preview a proposal in a chat before it is merged. Employees
/// never get here (D022). Every rule is the agents module's, through the same commands as <c>api/studio</c>.
/// </summary>
[Authorize(Roles = $"{SkanyxxRoles.Owner},{SkanyxxRoles.Supervisor},{SkanyxxRoles.Builder}")]
public sealed class StudioModel(IMediator mediator) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int? Pr { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Edit { get; set; }

    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public string? ModelConfig { get; set; }
    [BindProperty] public string? Instructions { get; set; }

    /// <summary>One OCI image reference per line.</summary>
    [BindProperty] public string? Skills { get; set; }

    /// <summary><c>server/tool</c> for each ticked MCP tool.</summary>
    [BindProperty] public List<string> Tools { get; set; } = [];

    /// <summary>Scopes ticked for search, and for upsert.</summary>
    [BindProperty] public List<string> Search { get; set; } = [];
    [BindProperty] public List<string> Upsert { get; set; } = [];

    [BindProperty] public int? TtlDays { get; set; }

    [BindProperty] public string? FactoryRequest { get; set; }

    public StudioOverview? Overview { get; private set; }
    public StudioFormOptions? Options { get; private set; }
    public ProposalDetail? Detail { get; private set; }
    public StudioUser Me => StudioUser.From(User);

    [TempData] public string? Message { get; set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(Edit) && await Validated(() => mediator.Send(new EditDraftQuery(Me, Edit), ct)) is { } edit)
        {
            if (edit.Value is { } draft)
                Fill(draft);
            else
                Fail(edit);
        }
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostProposeAsync(CancellationToken ct)
    {
        var outcome = await Validated(() => mediator.Send(new ProposeAgentCommand(Me, Draft().Normalized()), ct));
        if (outcome is { Status: OutcomeStatus.Created, Value: { } result })
        {
            Message = result.Message;
            return RedirectToPage(null, new { pr = result.Number });
        }
        if (outcome is not null)
            Fail(outcome);
        await LoadAsync(ct);
        return Page();
    }

    public Task<IActionResult> OnPostMergeAsync(CancellationToken ct) => ActAsync(() => mediator.Send(new MergeProposalCommand(Me, Pr ?? 0), ct), ct);

    public Task<IActionResult> OnPostCloseAsync(CancellationToken ct) => ActAsync(() => mediator.Send(new CloseProposalCommand(Me, Pr ?? 0), ct), ct);

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken ct)
    {
        var outcome = await Validated(() => mediator.Send(new StartPreviewCommand(Me, Pr ?? 0), ct));
        if (outcome is { Status: OutcomeStatus.Ok, Value: { } preview })
        {
            Message = $"Preview {preview.Name} is {(preview.Ready ? "ready" : "starting (about a minute)")}. It never writes to memory and is not in Chat.";
            return RedirectToPage(null, new { pr = Pr });
        }
        if (outcome is not null)
            Fail(outcome);
        await LoadAsync(ct);
        return Page();
    }

    /// <summary>The factory fills this form (D5); the builder reviews and proposes it like any other.</summary>
    public async Task<IActionResult> OnPostFactoryAsync(CancellationToken ct)
    {
        var outcome = await Validated(() => mediator.Send(new FactoryDraftCommand(Me, FactoryRequest?.Trim() ?? ""), ct));
        if (outcome?.Value is { } draft)
        {
            Fill(draft);
            Message = "The factory drafted this form. Review it, then Propose.";
        }
        else if (outcome is not null)
            Fail(outcome);
        ModelState.Clear(); // render the drafted values, not the posted (empty) ones
        await LoadAsync(ct);
        return Page();
    }

    public bool Ticked(List<string> list, string value) => list.Contains(value);

    /// <summary>Merge and close end the proposal: success goes back to the list (post/redirect/get).</summary>
    private async Task<IActionResult> ActAsync(Func<Task<Outcome<StudioResult>>> send, CancellationToken ct)
    {
        var outcome = await Validated(send);
        if (outcome is { Value: { } result } && outcome.Status is OutcomeStatus.Ok or OutcomeStatus.Accepted)
        {
            Message = result.Message;
            return RedirectToPage(null, new { pr = (int?)null });
        }
        if (outcome is not null)
            Fail(outcome);
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var overview = await mediator.Send(new StudioOverviewQuery(Me), ct);
        Overview = overview.Value;
        if (Overview is null)
            Fail(overview);
        if (Overview is { RepoProblem: null })
        {
            var options = await mediator.Send(new StudioFormOptionsQuery(Me), ct);
            Options = options.Value;
            if (Options is null)
                Fail(options);
        }
        if (Pr is > 0 && Overview is { RepoProblem: null })
        {
            var detail = await Validated(() => mediator.Send(new ProposalQuery(Me, Pr.Value), ct));
            Detail = detail?.Value;
            if (detail is { Value: null })
                Fail(detail);
        }
    }

    private AgentDraft Draft()
    {
        var tools = Tools.Select(t => t.Split('/', 2)).Where(p => p.Length == 2)
            .GroupBy(p => p[0]).Select(g => new McpToolChoice(g.Key, [.. g.Select(p => p[1])])).ToList();
        var scopes = Search.Concat(Upsert).Distinct().ToList();
        return new AgentDraft(Name ?? "", Description ?? "", ModelConfig ?? "", Instructions ?? "",
            [.. (Skills ?? "").Split('\n')], tools,
            [.. scopes.Select(s => new StudioGrant(s, Search.Contains(s), Upsert.Contains(s)))], TtlDays);
    }

    private void Fill(AgentDraft draft)
    {
        Name = draft.Name;
        Description = draft.Description;
        ModelConfig = draft.ModelConfig;
        Instructions = draft.Instructions;
        Skills = string.Join('\n', draft.Skills);
        Tools = [.. draft.McpTools.SelectMany(t => t.Tools.Select(n => $"{t.Server}/{n}"))];
        Search = [.. draft.Grants.Where(g => g.Search).Select(g => g.Scope)];
        Upsert = [.. draft.Grants.Where(g => g.Upsert).Select(g => g.Scope)];
        TtlDays = draft.MemoryTtlDays;
    }

    private void Fail<T>(Outcome<T> outcome) => Fail(outcome.Message, outcome.Status.HttpStatus());

    /// <summary>The first failure wins, and its status is the response's.</summary>
    private void Fail(string? message, int status)
    {
        if (Error is not null)
            return;
        Error = message;
        Response.StatusCode = status;
    }

    private async Task<T?> Validated<T>(Func<Task<T>> action)
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
