namespace Skanyxx.Module.Quotas.Contracts;

/// <summary>The caller's own month, their requests, and the cost disclaimer that goes with any cost.</summary>
public sealed record MyQuotaDto(
    string Month, DateOnly ResetsOn, PersonQuotaDto Quota, IReadOnlyList<QuotaRequestDto> Requests, bool HasPending,
    string Currency, string CostDisclaimerTitle, string CostDisclaimer);
