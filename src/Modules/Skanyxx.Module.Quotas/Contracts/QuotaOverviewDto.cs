namespace Skanyxx.Module.Quotas.Contracts;

/// <summary>
/// The Quotas panel: the people the caller may see (everyone for the owner, their teams for a supervisor), every limit
/// on them, the requests to decide, and the prices. <see cref="IsOwner"/>: may set team and department limits and prices.
/// <see cref="ActFor"/>: the people whose personal limit and requests this viewer may change (D338).
/// </summary>
public sealed record QuotaOverviewDto(
    string Month, DateOnly ResetsOn, bool IsOwner, IReadOnlyList<PersonQuotaDto> People, IReadOnlyList<LimitDto> Limits,
    IReadOnlyList<ScopeOption> Scopes, IReadOnlyList<QuotaRequestDto> Requests, IReadOnlyList<PriceDto> Prices, IReadOnlyList<string> Models,
    IReadOnlyList<string> ActFor, string Currency, string CostDisclaimerTitle, string CostDisclaimer);
