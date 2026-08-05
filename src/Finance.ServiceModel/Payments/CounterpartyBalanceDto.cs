namespace Finance.ServiceModel.Payments;

/// <summary>
/// One counterparty's outstanding and overdue summary for a single currency (SDD-PAY-003 §2.7). Rows are keyed by
/// the PAIR (<see cref="CounterpartyId"/>, <see cref="CurrencyCode"/>), so a multi-currency counterparty produces
/// one row per currency and no cross-currency transactional total is ever emitted.
/// <para>A counterparty with zero in-scope outstanding is omitted from the page and is not counted in the total
/// count — an unknown counterparty simply yields an empty page with <c>200</c>, because the counterparty is
/// Warehouse-owned master data this service deliberately does not pre-check.</para>
/// <para>For any (counterparty, currency) pair this row's <see cref="Outstanding"/> equals the aging report's
/// total outstanding for the same as-of date and direction: both endpoints share ONE aggregation path so they
/// cannot drift. Balances are NEVER cached.</para>
/// </summary>
public sealed record CounterpartyBalanceDto
{
    /// <summary>The Warehouse-owned counterparty reference. v1 returns the GUID only — name enrichment is deferred.</summary>
    public required Guid CounterpartyId { get; init; }

    /// <summary>The transactional currency this row aggregates; half of the grouping key.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The base currency the row's items book in.</summary>
    public required string BaseCurrencyCode { get; init; }

    /// <summary>The reported direction (<c>AR</c> or <c>AP</c>).</summary>
    public required string Direction { get; init; }

    /// <summary>
    /// The number of in-scope open items behind the row. A fully settled invoice vanishes from this count while
    /// remaining visible in the allocation views; deallocating makes it reappear.
    /// </summary>
    public required int OpenItemCount { get; init; }

    /// <summary>The total outstanding in the row's transactional currency.</summary>
    public required decimal Outstanding { get; init; }

    /// <summary>The total outstanding converted at each item's frozen booking rate.</summary>
    public required decimal BaseOutstanding { get; init; }

    /// <summary>
    /// The subset of <see cref="Outstanding"/> whose items are at least one day past due — i.e. the sum of every
    /// non-<c>Current</c> aging bucket for this row.
    /// </summary>
    public required decimal OverdueOutstanding { get; init; }

    /// <summary>The base-currency counterpart of <see cref="OverdueOutstanding"/>.</summary>
    public required decimal BaseOverdueOutstanding { get; init; }

    /// <summary>
    /// The earliest due date among the counterparty's in-scope open items, or <c>null</c> when there are none.
    /// Items issued after the as-of date are excluded from this date exactly as they are from the totals.
    /// </summary>
    public DateTimeOffset? OldestDueDate { get; init; }
}
