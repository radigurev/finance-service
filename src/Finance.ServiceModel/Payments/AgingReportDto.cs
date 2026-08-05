namespace Finance.ServiceModel.Payments;

/// <summary>
/// The bucketed AP/AR aging report for ONE direction as of a date (SDD-PAY-003 §2.6). It is a read-only roll-up
/// over the payments sub-ledger: it changes no state, publishes no event, writes no audit row, and is NEVER
/// cached — it is derived from transactional data, so every request recomputes from the current projection state.
/// <para><b>Sub-ledger, not general ledger.</b> On consistent books the total <c>AR</c> outstanding corresponds
/// to the GL customers control account and the total <c>AP</c> outstanding to the suppliers control account, but
/// v1 asserts no reconciliation — the two live in different services. For a foreign-currency document the two
/// figures are not even expected to agree yet, because the shipped posting path is still currency-naive.</para>
/// <para><b>Eventually consistent.</b> The projection is fed by the invoice service's domain events, so a very
/// recently confirmed invoice may be absent and a very recently cancelled or reversed one may still be counted.
/// A confirmed CREDIT NOTE, by contrast, is absent permanently and by design: no payment can settle one.</para>
/// <para><b>Invoice-only.</b> Unallocated payment cash is NOT netted into any total in v1, so a counterparty
/// sitting on on-account cash still shows its full invoice outstanding and no balance is ever negative.</para>
/// </summary>
public sealed record AgingReportDto
{
    /// <summary>The effective as-of date: the inclusive upper bound of the accounting view and the reference date for bucketing.</summary>
    public required DateTimeOffset AsOfDate { get; init; }

    /// <summary>The reported direction (<c>AR</c> or <c>AP</c>).</summary>
    public required string Direction { get; init; }

    /// <summary>The reporting base currency supplied by the country strategy; the only currency grand totals use.</summary>
    public required string BaseCurrencyCode { get; init; }

    /// <summary>
    /// The effective ascending day boundaries the buckets were derived from (<c>30, 60, 90</c> unless the caller
    /// supplied its own), echoed so a client never re-derives them.
    /// </summary>
    public required IReadOnlyList<int> BucketDayBoundaries { get; init; }

    /// <summary>The effective bucket labels in bucket order, echoed alongside the boundaries.</summary>
    public required IReadOnlyList<string> BucketLabels { get; init; }

    /// <summary>
    /// The per-counterparty rows, keyed by (counterparty, currency) and ordered by total base outstanding
    /// descending, then counterparty, then currency. Empty when nothing is in scope — that is a <c>200</c>, never
    /// a <c>404</c>.
    /// </summary>
    public required IReadOnlyList<AgingRowDto> Rows { get; init; }

    /// <summary>The report-level per-bucket totals in bucket order, expressed in BASE currency only.</summary>
    public required IReadOnlyList<AgingBucketTotalDto> Totals { get; init; }

    /// <summary>The sum of every bucket total, in the reporting base currency.</summary>
    public required decimal GrandTotalBaseOutstanding { get; init; }

    /// <summary>The number of in-scope open items behind the whole report.</summary>
    public required int OpenItemCount { get; init; }
}
