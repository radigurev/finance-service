namespace Finance.ServiceModel.Payments;

/// <summary>
/// The query parameters of <c>GET /api/v1/counterparty-balances</c> (SDD-PAY-003 §2.7), bound from the query
/// string alongside the SDD-INFRA-005 <c>FilterRequest</c> that carries paging.
/// <para><see cref="AsOfDate"/> and <see cref="Direction"/> are REQUIRED; the currency narrowing is optional.
/// There is deliberately no counterparty narrowing here — the endpoint IS the per-counterparty roll-up, and a
/// single counterparty's detail is read through <c>GET /api/v1/open-items</c>.</para>
/// </summary>
public sealed record CounterpartyBalanceQueryRequest
{
    /// <summary>
    /// REQUIRED. The inclusive upper bound of the accounting view and the reference date for the overdue split. A
    /// missing or FUTURE value is rejected before any query runs.
    /// </summary>
    public DateTimeOffset? AsOfDate { get; init; }

    /// <summary>REQUIRED. The ledger direction to report: <c>AR</c> or <c>AP</c>.</summary>
    public string? Direction { get; init; }

    /// <summary>The optional transactional currency narrowing; a three-letter ISO 4217 code when supplied.</summary>
    public string? CurrencyCode { get; init; }
}
